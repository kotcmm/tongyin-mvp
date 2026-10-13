package main

import (
	"bytes"
	"crypto"
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"io"
	"net/http"
	"os"
	"strconv"
	"time"
)

func (a *App) payReady() bool {
	c := a.cfg
	return c.AppID != "" && c.MchID != "" && c.MchSerial != "" && c.MchPrivateKeyPath != "" && c.PlatformPublicKeyPath != "" && c.PayAPIKey != "" && c.PayNotifyURL != ""
}
func readRSAKey(path string) (*rsa.PrivateKey, error) {
	b, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	p, _ := pem.Decode(b)
	if p == nil {
		return nil, fmt.Errorf("私钥不是 PEM")
	}
	if key, err := x509.ParsePKCS8PrivateKey(p.Bytes); err == nil {
		r, ok := key.(*rsa.PrivateKey)
		if !ok {
			return nil, fmt.Errorf("不是 RSA 私钥")
		}
		return r, nil
	}
	return x509.ParsePKCS1PrivateKey(p.Bytes)
}
func readRSAPublic(path string) (*rsa.PublicKey, error) {
	b, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	p, _ := pem.Decode(b)
	if p == nil {
		return nil, fmt.Errorf("公钥或平台证书不是 PEM")
	}
	if cert, err := x509.ParseCertificate(p.Bytes); err == nil {
		r, ok := cert.PublicKey.(*rsa.PublicKey)
		if !ok {
			return nil, fmt.Errorf("非 RSA 证书")
		}
		return r, nil
	}
	key, err := x509.ParsePKIXPublicKey(p.Bytes)
	if err != nil {
		return nil, err
	}
	r, ok := key.(*rsa.PublicKey)
	if !ok {
		return nil, fmt.Errorf("不是 RSA 公钥")
	}
	return r, nil
}
func rsaSign(key *rsa.PrivateKey, message string) (string, error) {
	sum := sha256.Sum256([]byte(message))
	sig, err := rsa.SignPKCS1v15(rand.Reader, key, crypto.SHA256, sum[:])
	if err != nil {
		return "", err
	}
	return base64.StdEncoding.EncodeToString(sig), nil
}
func (a *App) startPay(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	o, ok := a.db.order(r.PathValue("id"))
	if !ok || o.Owner != owner {
		fail(w, 404, "订单不存在")
		return
	}
	if o.Status != "awaiting_payment" {
		fail(w, 409, "订单状态不允许支付")
		return
	}
	if !a.payReady() {
		fail(w, 503, "微信商户配置尚未完成，暂时不能付款")
		return
	}
	private, err := readRSAKey(a.cfg.MchPrivateKeyPath)
	if err != nil {
		fail(w, 503, "商户私钥配置错误")
		return
	}
	params := map[string]any{"appid": a.cfg.AppID, "mchid": a.cfg.MchID, "description": "童印 · 儿童画定制白模", "out_trade_no": o.ID, "notify_url": a.cfg.PayNotifyURL, "amount": map[string]any{"total": o.PriceFen, "currency": "CNY"}, "payer": map[string]string{"openid": owner}}
	// WeChat amount only accepts total + currency string.
	body, _ := json.Marshal(params)
	path := "/v3/pay/transactions/jsapi"
	ts := strconv.FormatInt(time.Now().Unix(), 10)
	nonce := newID()
	sign, err := rsaSign(private, "POST\n"+path+"\n"+ts+"\n"+nonce+"\n"+string(body)+"\n")
	if err != nil {
		fail(w, 500, "签名失败")
		return
	}
	req, _ := http.NewRequest("POST", "https://api.mch.weixin.qq.com"+path, bytes.NewReader(body))
	req.Header.Set("Authorization", fmt.Sprintf(`WECHATPAY2-SHA256-RSA2048 mchid="%s",nonce_str="%s",timestamp="%s",serial_no="%s",signature="%s"`, a.cfg.MchID, nonce, ts, a.cfg.MchSerial, sign))
	req.Header.Set("Accept", "application/json")
	req.Header.Set("Content-Type", "application/json")
	res, err := a.client.Do(req)
	if err != nil {
		fail(w, 502, "创建微信支付订单失败")
		return
	}
	defer res.Body.Close()
	raw, _ := io.ReadAll(io.LimitReader(res.Body, 8192))
	if res.StatusCode != 200 {
		fail(w, 502, "微信支付预下单失败："+string(raw))
		return
	}
	var parsed struct {
		PrepayID string `json:"prepay_id"`
	}
	if json.Unmarshal(raw, &parsed) != nil || parsed.PrepayID == "" {
		fail(w, 502, "微信预支付数据不正确")
		return
	}
	clientTS := strconv.FormatInt(time.Now().Unix(), 10)
	clientNonce := newID()
	pkg := "prepay_id=" + parsed.PrepayID
	paySign, err := rsaSign(private, a.cfg.AppID+"\n"+clientTS+"\n"+clientNonce+"\n"+pkg+"\n")
	if err != nil {
		fail(w, 500, "支付签名失败")
		return
	}
	reply(w, 200, map[string]string{"timeStamp": clientTS, "nonceStr": clientNonce, "package": pkg, "signType": "RSA", "paySign": paySign})
}
func verifyNotification(pub *rsa.PublicKey, ts, nonce, signature string, body []byte) error {
	sig, err := base64.StdEncoding.DecodeString(signature)
	if err != nil {
		return err
	}
	sum := sha256.Sum256([]byte(ts + "\n" + nonce + "\n" + string(body) + "\n"))
	return rsa.VerifyPKCS1v15(pub, crypto.SHA256, sum[:], sig)
}
func (a *App) payNotify(w http.ResponseWriter, r *http.Request) {
	if !a.payReady() {
		fail(w, 503, "支付回调尚未配置")
		return
	}
	if a.cfg.PlatformSerial != "" && r.Header.Get("Wechatpay-Serial") != a.cfg.PlatformSerial {
		fail(w, 401, "平台序列号不符")
		return
	}
	pub, err := readRSAPublic(a.cfg.PlatformPublicKeyPath)
	if err != nil {
		fail(w, 500, "平台验签密钥不可用")
		return
	}
	ts := r.Header.Get("Wechatpay-Timestamp")
	n := r.Header.Get("Wechatpay-Nonce")
	t, err := strconv.ParseInt(ts, 10, 64)
	if err != nil || time.Now().Sub(time.Unix(t, 0)) > 5*time.Minute || time.Until(time.Unix(t, 0)) > 5*time.Minute {
		fail(w, 401, "回调时间戳无效")
		return
	}
	body, err := io.ReadAll(io.LimitReader(r.Body, 1<<20))
	if err != nil || len(body) > 1<<20 {
		fail(w, 400, "回调过大")
		return
	}
	if err := verifyNotification(pub, ts, n, r.Header.Get("Wechatpay-Signature"), body); err != nil {
		fail(w, 401, "验签失败")
		return
	}
	var evt struct {
		EventType string `json:"event_type"`
		Resource  struct {
			Algorithm, Ciphertext, Nonce, AssociatedData string `json:"algorithm"`
		}
	}
	// Resource field needs explicit json labels.
	var rawEvent struct {
		EventType string `json:"event_type"`
		Resource  struct {
			Algorithm      string `json:"algorithm"`
			Ciphertext     string `json:"ciphertext"`
			Nonce          string `json:"nonce"`
			AssociatedData string `json:"associated_data"`
		} `json:"resource"`
	}
	_ = evt
	if json.Unmarshal(body, &rawEvent) != nil {
		fail(w, 400, "回调内容无效")
		return
	}
	if rawEvent.EventType != "TRANSACTION.SUCCESS" {
		reply(w, 200, map[string]string{"code": "SUCCESS", "message": "成功"})
		return
	}
	if rawEvent.Resource.Algorithm != "AEAD_AES_256_GCM" || len(a.cfg.PayAPIKey) != 32 {
		fail(w, 500, "回调加密配置错误")
		return
	}
	block, err := aes.NewCipher([]byte(a.cfg.PayAPIKey))
	if err != nil {
		fail(w, 500, "AES 配置错误")
		return
	}
	gcm, err := cipher.NewGCM(block)
	if err != nil {
		fail(w, 500, "GCM 错误")
		return
	}
	encrypted, err := base64.StdEncoding.DecodeString(rawEvent.Resource.Ciphertext)
	if err != nil {
		fail(w, 400, "密文无效")
		return
	}
	plain, err := gcm.Open(nil, []byte(rawEvent.Resource.Nonce), encrypted, []byte(rawEvent.Resource.AssociatedData))
	if err != nil {
		fail(w, 401, "回调解密失败")
		return
	}
	var tx struct {
		OutTradeNo, TradeState, AppID, MchID string
		Amount                               struct {
			Total int `json:"total"`
		} `json:"amount"`
		Payer struct {
			OpenID string `json:"openid"`
		} `json:"payer"`
	}
	if json.Unmarshal(plain, &tx) != nil {
		fail(w, 400, "交易内容无效")
		return
	}
	// Unmarshal underscore JSON keys separately; Go field names won't map automatically.
	var verified struct {
		OutTradeNo string `json:"out_trade_no"`
		TradeState string `json:"trade_state"`
		AppID      string `json:"appid"`
		MchID      string `json:"mchid"`
		Amount     struct {
			Total int `json:"total"`
		} `json:"amount"`
		Payer struct {
			OpenID string `json:"openid"`
		} `json:"payer"`
	}
	_ = tx
	if json.Unmarshal(plain, &verified) != nil {
		fail(w, 400, "交易解码失败")
		return
	}
	if verified.TradeState != "SUCCESS" || verified.AppID != a.cfg.AppID || verified.MchID != a.cfg.MchID {
		fail(w, 409, "交易商户信息不符")
		return
	}
	err = a.db.update(func(d *Data) error {
		o, ok := d.Orders[verified.OutTradeNo]
		if !ok {
			return errNotFound
		}
		if o.Owner != verified.Payer.OpenID || o.PriceFen != verified.Amount.Total {
			return fmt.Errorf("订单身份或金额不符")
		}
		if o.Status == "awaiting_payment" {
            o.Status="paid"
            o.History=append(o.History,OrderHistory{At:now(),Actor:"wechatpay",Action:"paid",Note:"微信支付通知已验签"})
            recordAudit(d,"wechatpay","order.paid",o.ID,"微信支付验签通过")
        }
		return nil
	})
	if err != nil {
		fail(w, 409, "未找到匹配的订单")
		return
	}
	reply(w, 200, map[string]string{"code": "SUCCESS", "message": "成功"})
}
