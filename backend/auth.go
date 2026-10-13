package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

func (a *App) token(openid string) string {
	ts := strconv.FormatInt(time.Now().Add(30*24*time.Hour).Unix(), 10)
	payload := base64.RawURLEncoding.EncodeToString([]byte(openid + "|" + ts))
	mac := hmac.New(sha256.New, []byte(a.cfg.SessionKey))
	mac.Write([]byte(payload))
	return payload + "." + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}
func (a *App) auth(r *http.Request) (string, error) {
	tok := strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer ")
	parts := strings.Split(tok, ".")
	if len(parts) != 2 {
		return "", errors.New("请登录")
	}
	sig, err := base64.RawURLEncoding.DecodeString(parts[1])
	if err != nil {
		return "", err
	}
	mac := hmac.New(sha256.New, []byte(a.cfg.SessionKey))
	mac.Write([]byte(parts[0]))
	if !hmac.Equal(mac.Sum(nil), sig) {
		return "", errors.New("登录已失效")
	}
	data, err := base64.RawURLEncoding.DecodeString(parts[0])
	if err != nil {
		return "", err
	}
	s := strings.Split(string(data), "|")
	if len(s) != 2 || s[0] == "" {
		return "", errors.New("登录无效")
	}
	exp, err := strconv.ParseInt(s[1], 10, 64)
	if err != nil || time.Now().Unix() > exp {
		return "", errors.New("登录已过期")
	}
	return s[0], nil
}
func (a *App) isAdmin(r *http.Request) bool {
	token := r.Header.Get("X-Admin-Token")
	return a.cfg.AdminToken != "" && subtle.ConstantTimeCompare([]byte(token), []byte(a.cfg.AdminToken)) == 1
}
func (a *App) login(w http.ResponseWriter, r *http.Request) {
	if r.Method != "POST" {
		http.Error(w, "method not allowed", 405)
		return
	}
	var req struct {
		Code string `json:"code"`
	}
	if err := decode(r, &req); err != nil {
		fail(w, 400, "参数不合法")
		return
	}
	if req.Code == "DEV_LOGIN" && a.cfg.Dev {
        if err:=a.ensureUser("dev-user");err!=nil{fail(w,403,err.Error());return}
        if err:=a.trackLogin("dev-user");err!=nil{fail(w,500,"登录记录失败");return}
        reply(w, 200, map[string]string{"token": a.token("dev-user")})
		return
	}
	if a.cfg.AppID == "" || a.cfg.AppSecret == "" {
		fail(w, 503, "尚未配置微信 AppID/AppSecret")
		return
	}
	endpoint := "https://api.weixin.qq.com/sns/jscode2session?appid=" + url.QueryEscape(a.cfg.AppID) + "&secret=" + url.QueryEscape(a.cfg.AppSecret) + "&js_code=" + url.QueryEscape(req.Code) + "&grant_type=authorization_code"
	client := http.Client{Timeout: 10 * time.Second}
	res, err := client.Get(endpoint)
	if err != nil {
		fail(w, 502, "微信登录请求失败")
		return
	}
	defer res.Body.Close()
	var v struct {
		OpenID  string `json:"openid"`
		ErrCode int    `json:"errcode"`
		ErrMsg  string `json:"errmsg"`
	}
	if res.StatusCode!=200 || json.NewDecoder(res.Body).Decode(&v) != nil || v.OpenID == "" || v.ErrCode!=0 {
		fail(w, 401, "微信登录失败，请重试")
		return
	}
	if err:=a.ensureUser(v.OpenID);err!=nil{fail(w,403,err.Error());return}
    if err:=a.trackLogin(v.OpenID);err!=nil{fail(w,500,"登录记录失败");return}
    reply(w, 200, map[string]string{"token": a.token(v.OpenID)})
}
