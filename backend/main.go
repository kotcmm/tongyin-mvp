package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"errors"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

type App struct {
	cfg    Config
	db     Repository
	client *http.Client
}

func main() {
	c := config()
	if c.AdminToken == "" || len(c.SessionKey) < 24 {
		log.Fatal("配置 ADMIN_TOKEN 和长度至少24位的 SESSION_SECRET 后启动")
	}
	if !c.Dev && (!strings.HasPrefix(c.PublicURL, "https://") || c.AppID == "" || c.AppSecret == "") {
		log.Fatal("生产模式需要 HTTPS PUBLIC_URL、WX_APP_ID、WX_APP_SECRET")
	}
	if err := os.MkdirAll(filepath.Join(c.DataDir, "media"), 0700); err != nil {
		log.Fatal(err)
	}
	db, err := openRepository(c)
	if err != nil {
		log.Fatal(err)
	}
	defer db.Close()
 if err:=bootstrapAdmin(db,c);err!=nil{log.Fatal(err)}
 if err:=backfillLegacyUsers(db);err!=nil{log.Fatal(err)}
 if err := activateLegacyGLBWorks(db); err != nil { log.Fatal(err) }
	app := &App{cfg: c, db: db, client: &http.Client{Timeout: 90 * time.Second}}
	s := &http.Server{Addr: c.Addr, Handler: app.routes(), ReadHeaderTimeout: 10 * time.Second, MaxHeaderBytes: 1 << 20}
	if c.AIWorkerURL == "" {
		log.Print("提示：AI_WORKER_URL 未配置，当前为后台人工处理模式；AI 任务不会自动生成结果")
	}
	log.Printf("童印 API listening on %s", c.Addr)
	log.Fatal(s.ListenAndServe())
}
// Older versions required GLB + preview images to become ready.
// When upgrading, make existing uploaded GLBs immediately viewable.
func activateLegacyGLBWorks(db Repository) error {
 needMigration := false
 for _, work := range db.works("", true) {
  if work.Status == "model_pending" && work.Model != "" { needMigration = true; break }
 }
 if !needMigration { return nil }
 return db.update(func(data *Data) error {
  for _, work := range data.Works {
   if work.Status == "model_pending" && work.Model != "" {
    work.Status = "model_ready"
   }
  }
  return nil
 })
}
func (a *App) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) { reply(w, 200, map[string]string{"status": "ok"}) })
	mux.HandleFunc("POST /api/auth/wechat", a.login)
 mux.HandleFunc("GET /api/me",a.customerMe)
 mux.HandleFunc("POST /api/admin/auth/login",a.adminLogin)
 mux.HandleFunc("GET /api/admin/me",a.adminMe)
 mux.HandleFunc("GET /api/admin/dashboard",a.adminDashboard)
 mux.HandleFunc("GET /api/admin/users",a.adminUsers)
 mux.HandleFunc("POST /api/admin/users/{id}/status",a.adminUserStatus)
 mux.HandleFunc("GET /api/admin/audit",a.adminAudit)
 mux.HandleFunc("GET /api/admin/accounts",a.adminAccounts)
 mux.HandleFunc("POST /api/admin/accounts",a.adminAccounts)
 mux.HandleFunc("PUT /api/admin/accounts/{id}",a.adminAccountUpdate)
	mux.HandleFunc("GET /api/config", func(w http.ResponseWriter, r *http.Request) {
		reply(w, 200, map[string]any{"price_fen": a.cfg.PriceFen, "dev": a.cfg.Dev, "ai_auto_enabled": a.cfg.AIWorkerURL != "", "free_image_attempts": a.imageLimit()})
	})
	mux.HandleFunc("GET /media/{file}", a.serveMedia)
	mux.HandleFunc("POST /api/works", a.createWork)
	mux.HandleFunc("GET /api/works", a.listWorks)
	mux.HandleFunc("GET /api/works/{id}", a.getWork)
    mux.HandleFunc("GET /api/works/{id}/order", a.getWorkOrder)
	mux.HandleFunc("POST /api/works/{id}/generate", a.startImageGeneration)
	mux.HandleFunc("POST /api/works/{id}/select", a.selectImage)
	mux.HandleFunc("GET /api/shipping-address", a.getShippingAddress)
    mux.HandleFunc("PUT /api/shipping-address", a.putShippingAddress)
    mux.HandleFunc("POST /api/orders", a.createOrder)
	mux.HandleFunc("GET /api/orders", a.listOrders)
	mux.HandleFunc("GET /api/orders/{id}", a.getOrder)
    mux.HandleFunc("PUT /api/orders/{id}/shipping-address", a.updateOrderShipping)
	mux.HandleFunc("POST /api/orders/{id}/pay", a.startPay)
 mux.HandleFunc("GET /api/orders/{id}/tracking",a.orderTracking)
 mux.HandleFunc("POST /api/admin/orders/{id}/shipment",a.adminShipment)
 mux.HandleFunc("POST /api/admin/orders/{id}/tracking",a.adminTrackingEntry)
 mux.HandleFunc("POST /api/admin/orders/{id}/tracking/refresh",a.adminTrackingRefresh)
 mux.HandleFunc("POST /api/admin/orders/{id}/refund",a.adminRefundRequest)
	mux.HandleFunc("POST /api/pay/notify", a.payNotify)
	mux.HandleFunc("GET /api/admin/works", a.adminWorks)
	mux.HandleFunc("POST /api/admin/works/{id}/asset", a.adminAsset)
	mux.HandleFunc("POST /api/admin/works/{id}/status", a.adminWorkStatus)
	mux.HandleFunc("GET /api/admin/orders", a.adminOrders)
	mux.HandleFunc("POST /api/admin/orders/{id}/status", a.adminOrderStatus)
	mux.HandleFunc("POST /api/internal/ai-result", a.aiResult)
	mux.HandleFunc("GET /admin", func(w http.ResponseWriter, r *http.Request) { http.Redirect(w, r, "/admin/", 302) })
	adminFiles := http.FileServer(http.Dir(env("ADMIN_DIR", "../admin")))
	mux.Handle("GET /admin/", http.StripPrefix("/admin/", http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		// On Windows the .mjs MIME type may be missing or provided by the
		// registry as application/octet-stream. Browsers refuse ES modules
		// unless the HTTP response advertises a JavaScript content type.
		if strings.HasSuffix(r.URL.Path, ".mjs") || strings.HasSuffix(r.URL.Path, ".js") {
			w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		}
		adminFiles.ServeHTTP(w, r)
	})))
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("Referrer-Policy", "no-referrer")
		// Do not enable cross-origin access globally. Admin is served from same origin.
		mux.ServeHTTP(w, r)
	})
}
func reply(w http.ResponseWriter, status int, x any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(x)
}
func fail(w http.ResponseWriter, status int, msg string) {
	reply(w, status, map[string]string{"error": msg})
}
func decode(r *http.Request, v any) error {
	r.Body = http.MaxBytesReader(nil, r.Body, 1<<20)
	return json.NewDecoder(r.Body).Decode(v)
}
func (a *App) uid(w http.ResponseWriter, r *http.Request) string {
	v, err := a.auth(r)
	if err != nil {
		fail(w, 401, err.Error())
		return ""
	}
	if err:=a.ensureUser(v);err!=nil{fail(w,403,err.Error());return ""}
 return v
}
func (a *App) admin(w http.ResponseWriter, r *http.Request) bool {
	_,role,ok:=a.adminIdentity(r)
 if !ok{fail(w,401,"管理员未登录或会话已失效");return false}
 if !adminAllowed(role,r){fail(w,403,"当前角色无权进行此操作");return false}
	return true
}
func (a *App) signedMedia(name string) string {
	if name == "" {
		return ""
	}
	exp := strconv.FormatInt(time.Now().Add(48*time.Hour).Unix(), 10)
	text := name + ":" + exp
	mac := hmac.New(sha256.New, []byte(a.cfg.SessionKey))
	mac.Write([]byte(text))
	return a.cfg.PublicURL + "/media/" + name + "?expires=" + exp + "&signature=" + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}
func (a *App) serveMedia(w http.ResponseWriter, r *http.Request) {
	name := r.PathValue("file")
	exp := r.URL.Query().Get("expires")
	sig := r.URL.Query().Get("signature")
	if name == "" || strings.ContainsAny(name, "/\\") || strings.Contains(name, "..") || len(name) > 100 {
		http.NotFound(w, r)
		return
	}
	t, e := strconv.ParseInt(exp, 10, 64)
	if e != nil || t < time.Now().Unix() {
		http.NotFound(w, r)
		return
	}
	raw, e := base64.RawURLEncoding.DecodeString(sig)
	if e != nil {
		http.NotFound(w, r)
		return
	}
	mac := hmac.New(sha256.New, []byte(a.cfg.SessionKey))
	mac.Write([]byte(name + ":" + exp))
	if !hmac.Equal(mac.Sum(nil), raw) {
		http.NotFound(w, r)
		return
	}
	w.Header().Set("Cache-Control", "private,max-age=120")
	http.ServeFile(w, r, filepath.Join(a.cfg.DataDir, "media", name))
}
func (a *App) exposeWork(wk *Work) *Work {
	cp := *wk
	cp.Owner = ""
	cp.Original = a.signedMedia(cp.Original)
	cp.Model = a.signedMedia(cp.Model)
	cp.Candidates = make([]string, len(wk.Candidates))
	for i, n := range wk.Candidates {
		cp.Candidates[i] = a.signedMedia(n)
	}
	cp.Preview = make([]string, len(wk.Preview))
	for i, n := range wk.Preview {
		cp.Preview[i] = a.signedMedia(n)
	}
	return &cp
}
// Image files are capped at 10 MiB; printable GLB models may be much larger.
// A small multipart overhead allowance is included in the request size limit.
const (
 maxImageUpload = int64(10 << 20)
 maxModelUpload = int64(100 << 20)
 multipartOverhead = int64(1 << 20)
)
var errUploadTooLarge = errors.New("上传文件超过允许大小")

func uploadFailure(w http.ResponseWriter, err error) {
 if errors.Is(err, errUploadTooLarge) {
  fail(w, http.StatusRequestEntityTooLarge, err.Error())
  return
 }
 fail(w, http.StatusBadRequest, err.Error())
}
func (a *App) upload(w http.ResponseWriter, r *http.Request, allowedImagesOnly bool) (string, error) {
 limit := maxModelUpload
 if allowedImagesOnly { limit = maxImageUpload }
 maxBody := limit + multipartOverhead
 if r.ContentLength > maxBody {
  return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
 }
 r.Body = http.MaxBytesReader(w, r.Body, maxBody)
 defer func() {
  if r.MultipartForm != nil { _ = r.MultipartForm.RemoveAll() }
 }()
 if err := r.ParseMultipartForm(8 << 20); err != nil {
  var maxErr *http.MaxBytesError
  if errors.As(err, &maxErr) {
   return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
  }
  return "", fmt.Errorf("解析上传文件失败：%w", err)
 }
 f, hdr, err := r.FormFile("file")
 if err != nil { return "", fmt.Errorf("未找到上传文件：%w", err) }
 defer f.Close()
 if hdr.Size > limit {
  return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
 }
 buf := make([]byte, 512)
 n, err := f.Read(buf)
 if err != nil && err != io.EOF { return "", err }
 if n == 0 { return "", fmt.Errorf("上传文件为空") }
 kind := http.DetectContentType(buf[:n])
 ext := ""
 switch kind {
 case "image/jpeg": ext = ".jpg"
 case "image/png": ext = ".png"
 case "image/webp": ext = ".webp"
 }
 if !allowedImagesOnly && n >= 4 && string(buf[:4]) == "glTF" { ext = ".glb" }
 if allowedImagesOnly && ext == "" {
  return "", fmt.Errorf("仅支持 JPG、PNG、WebP 图片")
 }
 if !allowedImagesOnly && ext != ".glb" {
  return "", fmt.Errorf("模型文件必须是 GLB 格式（不支持直接上传 STL、OBJ）")
 }
 name := newID()+ext
 path := filepath.Join(a.cfg.DataDir, "media", name)
 out, err := os.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
 if err != nil { return "", err }
 // Copy one byte beyond the limit so an oversized upload never succeeds as
 // a truncated, corrupted GLB. Close before removal on Windows.
 first, err := out.Write(buf[:n])
 total := int64(first)
 if err == nil {
  var copied int64
  copied, err = io.Copy(out, io.LimitReader(f, limit-total+1))
  total += copied
 }
 closeErr := out.Close()
 if err != nil || closeErr != nil || total > limit {
  _ = os.Remove(path)
  if total > limit {
   return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
  }
  if err != nil { return "", fmt.Errorf("写入上传文件失败：%w", err) }
  return "", fmt.Errorf("保存上传文件失败：%w", closeErr)
 }
 return name,nil
}
func (a *App) createWork(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	name, err := a.upload(w, r, true)
	if err != nil {
		uploadFailure(w, err)
		return
	}
	wk := &Work{ID: newID(), Owner: owner, CreatedAt: now(), Original: name, Candidates: []string{}, Selected: -1, Preview: []string{}, Status: "image_pending"}
	err = a.db.update(func(d *Data) error { d.Works[wk.ID] = wk; return nil })
	if err != nil {
		fail(w, 500, "保存失败")
		return
	}
	reply(w, 201, a.exposeWork(wk))
}
func (a *App) listWorks(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	out := []*Work{}
	for _, wk := range a.db.works(owner, false) {
		out = append(out, a.exposeWork(wk))
	}
	reply(w, 200, out)
}
func (a *App) getWork(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	wk, ok := a.db.work(r.PathValue("id"))
	if !ok || wk.Owner != owner {
		fail(w, 404, "作品不存在")
		return
	}
	reply(w, 200, a.exposeWork(wk))
}
func (a *App) imageLimit() int {
 if a.cfg.FreeImageAttempts > 0 { return a.cfg.FreeImageAttempts }
 return 2
}

// First generation and each retry create one image; cost is limited server-side.
func (a *App) startImageGeneration(w http.ResponseWriter, r *http.Request) {
 owner := a.uid(w, r)
 if owner == "" { return }
 id := r.PathValue("id")
 var task *Work
 err := a.db.update(func(d *Data) error {
  v, ok := d.Works[id]
  if !ok || v.Owner != owner { return errNotFound }
  if v.Status != "image_ready" && v.Status != "image_pending" && !(v.Status == "failed" && v.Selected < 0) {
   return fmt.Errorf("当前作品不能重新生成形象")
  }
  if v.Status == "image_pending" && v.ImageAttempts > 0 {
   return fmt.Errorf("形象正在生成，请勿重复提交")
  }
  if v.ImageAttempts >= a.imageLimit() {
   return fmt.Errorf("免费生成次数已用完，付费或广告获取次数暂未开放")
  }
  v.ImageAttempts++
  v.GenerationID = newID()
  v.Status = "image_pending"
  v.Candidates = []string{}
  v.Selected = -1
  v.Preview = []string{}
  v.Model = ""
  v.Note = ""
  copyWork := *v
  task = &copyWork
  return nil
 })
 if err != nil { fail(w, 409, err.Error()); return }
 a.dispatchAI(task, "image")
 reply(w, 202, map[string]any{"status":"image_pending", "image_attempts":task.ImageAttempts, "free_image_attempts":a.imageLimit(), "message":a.taskMessage()})
}
func (a *App) selectImage(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	id := r.PathValue("id")
	var q struct {
		Index int `json:"index"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数有误")
		return
	}
	var copyWork *Work
	err := a.db.update(func(d *Data) error {
		v, ok := d.Works[id]
		if !ok || v.Owner != owner {
			return errNotFound
		}
		if v.Status != "image_ready" || q.Index != 0 || len(v.Candidates) < 1 {
			return fmt.Errorf("无法选择此图片")
		}
		v.Candidates = v.Candidates[:1] // normalize works created before the single-image MVP
		v.Selected = q.Index
		v.Preview = []string{} // discard previews erroneously attached before selecting
		v.Model = ""
		v.Status = "model_pending"
		cp := *v
		copyWork = &cp
		return nil
	})
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	a.dispatchAI(copyWork, "model")
	reply(w, 202, map[string]string{"status": "model_pending", "message": a.taskMessage()})
}
// Shipping address is a single server-side default per authenticated WeChat OpenID.
// Existing customers are migrated on read from their most recent historical order.
func validateShipping(name, phone, address string) (string, string, string, error) {
 name=strings.TrimSpace(name)
 phone=strings.TrimSpace(phone)
 address=strings.TrimSpace(address)
 if len([]rune(name)) < 2 || len([]rune(name)) > 30 || len(phone)<6 || len(phone)>20 || len([]rune(address))<8 || len([]rune(address))>300 {
  return "","","",fmt.Errorf("请填写完整收货信息")
 }
 return name,phone,address,nil
}
func (a *App) lastShippingAddress(owner string) *ShippingAddress {
 if v,ok:=a.db.address(owner);ok && v.Owner==owner {return v}
 var latest *Order
 for _,o:=range a.db.orders(owner,false) {
  if o.Owner!=owner || o.Name=="" || o.Phone=="" || o.Address=="" {continue}
  if latest==nil || o.CreatedAt>latest.CreatedAt {latest=o}
 }
 if latest==nil {return nil}
 return &ShippingAddress{Owner:owner,Name:latest.Name,Phone:latest.Phone,Address:latest.Address,CreatedAt:latest.CreatedAt}
}
func (a *App) getShippingAddress(w http.ResponseWriter,r *http.Request) {
 owner:=a.uid(w,r)
 if owner=="" {return}
 addr:=a.lastShippingAddress(owner)
 if addr==nil {reply(w,200,map[string]any{"address":nil});return}
 cp:=*addr
 cp.Owner=""
 reply(w,200,map[string]any{"address":&cp})
}
func (a *App) putShippingAddress(w http.ResponseWriter,r *http.Request) {
 owner:=a.uid(w,r)
 if owner=="" {return}
 var q struct {Name string `json:"name"`; Phone string `json:"phone"`; Address string `json:"address"`}
 if decode(r,&q)!=nil {fail(w,400,"参数有误");return}
 name,phone,address,err:=validateShipping(q.Name,q.Phone,q.Address)
 if err!=nil {fail(w,400,err.Error());return}
 v:=&ShippingAddress{Owner:owner,CreatedAt:now(),Name:name,Phone:phone,Address:address}
 if err:=a.db.update(func(d *Data) error {
   if d.Addresses==nil {d.Addresses=map[string]*ShippingAddress{}}
   d.Addresses[owner]=v
   return nil
 });err!=nil {fail(w,500,"地址保存失败");return}
 cp:=*v
 cp.Owner=""
 reply(w,200,map[string]any{"address":&cp})
}
// Return the active order of this work without creating a duplicate.
func (a *App) getWorkOrder(w http.ResponseWriter, r *http.Request) {
 owner:=a.uid(w,r)
 if owner=="" {return}
 id:=r.PathValue("id")
 work,ok:=a.db.work(id)
 if !ok || work.Owner!=owner {fail(w,404,"作品不存在");return}
 var latest *Order
 for _,o:=range a.db.orders(owner,false) {
  if o.Owner!=owner || o.WorkID!=id || o.Status=="canceled" {continue}
  if latest==nil || o.CreatedAt>latest.CreatedAt {latest=o}
 }
 if latest==nil {reply(w,200,map[string]any{"order":nil});return}
 cp:=*latest
 cp.Owner=""
 reply(w,200,map[string]any{"order":&cp})
}

// Only an unpaid order's shipping snapshot may be updated online.
// Editing the order never rewrites a separately saved default address.
func (a *App) updateOrderShipping(w http.ResponseWriter, r *http.Request) {
 owner:=a.uid(w,r)
 if owner=="" {return}
 id:=r.PathValue("id")
 var q struct {
  Name string `json:"name"`
  Phone string `json:"phone"`
  Address string `json:"address"`
 }
 if decode(r,&q)!=nil {fail(w,400,"参数有误");return}
 name,phone,address,err:=validateShipping(q.Name,q.Phone,q.Address)
 if err!=nil {fail(w,400,err.Error());return}
 var updated Order
 err=a.db.update(func(d *Data) error {
  order,exists:=d.Orders[id]
  if !exists || order.Owner!=owner {return errNotFound}
  if order.Status!="awaiting_payment" {return fmt.Errorf("只有待付款订单可以修改收货地址")}
  order.Name=name
  order.Phone=phone
  order.Address=address
  updated=*order
  return nil
 })
 if errors.Is(err,errNotFound) {fail(w,404,"订单不存在");return}
 if err!=nil {fail(w,409,err.Error());return}
 updated.Owner=""
 reply(w,200,&updated)
}
func (a *App) createOrder(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	var q struct {
		WorkID  string `json:"work_id"`
		Name    string `json:"name"`
		Phone   string `json:"phone"`
		Address string `json:"address"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数有误")
		return
	}
	var validationErr error
    q.Name,q.Phone,q.Address,validationErr=validateShipping(q.Name,q.Phone,q.Address)
    if validationErr!=nil {fail(w,400,validationErr.Error());return}
	order := &Order{ID: newID(), Owner: owner, WorkID: q.WorkID, CreatedAt: now(), Name: q.Name, Phone: q.Phone, Address: q.Address, PriceFen: a.cfg.PriceFen, Status: "awaiting_payment"}
	err := a.db.update(func(d *Data) error {
		wk, ok := d.Works[q.WorkID]
		if !ok || wk.Owner != owner || wk.Status != "model_ready" {
			return fmt.Errorf("模型尚未准备好")
		}
		for _, o := range d.Orders {
			if o.WorkID == q.WorkID && o.Owner == owner && o.Status != "canceled" {
				return fmt.Errorf("该作品已有订单")
			}
		}
		d.Orders[order.ID] = order
        if d.Addresses==nil {d.Addresses=map[string]*ShippingAddress{}}
        d.Addresses[owner]=&ShippingAddress{Owner:owner,Name:q.Name,Phone:q.Phone,Address:q.Address,CreatedAt:now()}
		return nil
	})
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	copyOrder := *order
	copyOrder.Owner = ""
	reply(w, 201, &copyOrder)
}
func (a *App) listOrders(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	out := []*Order{}
	for _, o := range a.db.orders(owner, false) {
		o.Owner = ""
		out = append(out, o)
	}
	reply(w, 200, out)
}
func (a *App) getOrder(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	o, ok := a.db.order(r.PathValue("id"))
	if !ok || o.Owner != owner {
		fail(w, 404, "订单不存在")
		return
	}
	o.Owner = ""
	reply(w, 200, o)
}
