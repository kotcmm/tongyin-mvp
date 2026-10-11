package main

import (
	"bytes"
	"encoding/json"
	"image"
	"image/color"
	"image/png"
	"mime/multipart"
	"net/http"
	"net/http/httptest"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func mustJSON(t *testing.T, r *httptest.ResponseRecorder, status int, out any) {
	t.Helper()
	if r.Code != status {
		t.Fatalf("status %d != %d: %s", r.Code, status, r.Body.String())
	}
	if out != nil {
		if err := json.Unmarshal(r.Body.Bytes(), out); err != nil {
			t.Fatal(err)
		}
	}
}
func postFile(t *testing.T, mux http.Handler, url, token string, admin bool, content []byte, name string) *httptest.ResponseRecorder {
	t.Helper()
	var b bytes.Buffer
	form := multipart.NewWriter(&b)
	f, err := form.CreateFormFile("file", name)
	if err != nil {
		t.Fatal(err)
	}
	_, _ = f.Write(content)
	_ = form.Close()
	r := httptest.NewRequest("POST", url, &b)
	r.Header.Set("Content-Type", form.FormDataContentType())
	if admin {
		r.Header.Set("X-Admin-Token", "admin-secret")
	} else {
		r.Header.Set("Authorization", "Bearer "+token)
	}
	w := httptest.NewRecorder()
	mux.ServeHTTP(w, r)
	return w
}
func TestCompleteOrderUntilPay(t *testing.T) {
	cfg := Config{DataDir: t.TempDir(), PublicURL: "http://localhost:8080", AdminToken: "admin-secret", SessionKey: "test-secret-longer-than-24-chars", PriceFen: 13900, Dev: true}
	_ = os.MkdirAll(filepath.Join(cfg.DataDir, "media"), 0700)
	db, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	a := &App{cfg: cfg, db: db}
	mux := a.routes()
	token := a.token("openid-test")
	pngbuf := new(bytes.Buffer)
	img := image.NewRGBA(image.Rect(0, 0, 6, 6))
	img.Set(0, 0, color.RGBA{R: 255, A: 255})
	if err := png.Encode(pngbuf, img); err != nil {
		t.Fatal(err)
	}
	original := postFile(t, mux, "/api/works", token, false, pngbuf.Bytes(), "child.png")
	var work Work
	mustJSON(t, original, 201, &work)
	if work.Owner != "" || work.Status != "image_pending" {
		t.Fatalf("invalid initial work: %#v", work)
	}
	originalURL, err := url.Parse(work.Original)
	if err != nil {
		t.Fatal(err)
	}
	reqMedia := httptest.NewRequest("GET", originalURL.RequestURI(), nil)
	mediaResp := httptest.NewRecorder()
	mux.ServeHTTP(mediaResp, reqMedia)
	if mediaResp.Code != 200 {
		t.Fatalf("signed media did not load: %d", mediaResp.Code)
	}
	makeReq := func(method, path, body string, admin bool) *httptest.ResponseRecorder {
		r := httptest.NewRequest(method, path, strings.NewReader(body))
		r.Header.Set("Content-Type", "application/json")
		if admin {
			r.Header.Set("X-Admin-Token", "admin-secret")
		} else {
			r.Header.Set("Authorization", "Bearer "+token)
		}
		w := httptest.NewRecorder()
		mux.ServeHTTP(w, r)
		return w
	}
	mustJSON(t, makeReq("POST", "/api/works/"+work.ID+"/generate", "{}", false), 202, nil)
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=candidate", "", true, pngbuf.Bytes(), "candidate.png"), 200, nil)
	var ready Work
	mustJSON(t, makeReq("GET", "/api/works/"+work.ID, "", false), 200, &ready)
	if ready.Status != "image_ready" || len(ready.Candidates) != 1 {
		t.Fatal("image stage failed")
	}
	mustJSON(t, makeReq("POST", "/api/works/"+work.ID+"/select", `{"index":0}`, false), 202, nil)
	glb := append([]byte("glTF"), make([]byte, 16)...)
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=model", "", true, glb, "model.glb"), 200, nil)
	mustJSON(t, makeReq("GET", "/api/works/"+work.ID, "", false), 200, &ready)
	if ready.Status != "model_ready" || ready.Model == "" || len(ready.Preview) != 0 {
		t.Fatalf("GLB-only ready expected: %+v", ready)
	}
	var order Order
	mustJSON(t, makeReq("POST", "/api/orders", `{"work_id":"`+work.ID+`","name":"张三","phone":"13800138000","address":"广东省广州市天河区测试路100号"}`, false), 201, &order)
	if order.Status != "awaiting_payment" || order.PriceFen != 13900 {
		t.Fatalf("order wrong: %#v", order)
	}
	mustJSON(t, makeReq("POST", "/api/orders/"+order.ID+"/pay", "{}", false), 503, nil)
	mustJSON(t, makeReq("POST", "/api/admin/orders/"+order.ID+"/status", `{"Status":"printing"}`, true), 409, nil)
	reloaded, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	saved, ok := reloaded.order(order.ID)
	if !ok || saved.Owner != "openid-test" {
		t.Fatal("lost persisted order owner")
	}
}

func TestOneImageAndFreeRetryLimit(t *testing.T) {
 cfg := Config{DataDir:t.TempDir(),PublicURL:"http://localhost:8080",AdminToken:"admin-secret",SessionKey:"test-secret-longer-than-24-chars",Dev:true,FreeImageAttempts:2}
 _ = os.MkdirAll(filepath.Join(cfg.DataDir,"media"),0700)
 db,err:=newStore(filepath.Join(cfg.DataDir,"store.json"));if err!=nil{t.Fatal(err)}
 a:=&App{cfg:cfg,db:db};mux:=a.routes();token:=a.token("test-owner")
 pic:=new(bytes.Buffer);_ = png.Encode(pic,image.NewRGBA(image.Rect(0,0,4,4)))
 uploaded:=postFile(t,mux,"/api/works",token,false,pic.Bytes(),"drawing.png")
 var work Work;mustJSON(t,uploaded,201,&work)
 req:=func(method,path string)*httptest.ResponseRecorder{
  r:=httptest.NewRequest(method,path,strings.NewReader("{}"));r.Header.Set("Authorization","Bearer "+token)
  w:=httptest.NewRecorder();mux.ServeHTTP(w,r);return w
 }
 // No model may be uploaded before selecting the sole image.
 mustJSON(t,postFile(t,mux,"/api/admin/works/"+work.ID+"/asset?kind=model","",true,append([]byte("glTF"),make([]byte,12)...),"x.glb"),409,nil)
 mustJSON(t,req("POST","/api/works/"+work.ID+"/generate"),202,nil)
 mustJSON(t,req("POST","/api/works/"+work.ID+"/generate"),409,nil) // pending task is not duplicated
 mustJSON(t,postFile(t,mux,"/api/admin/works/"+work.ID+"/asset?kind=candidate","",true,pic.Bytes(),"image.png"),200,nil)
 v,ok:=a.db.work(work.ID)
 if !ok||v.Status!="image_ready"||len(v.Candidates)!=1||v.ImageAttempts!=1 {t.Fatalf("first image: %+v",v)}
 mustJSON(t,req("POST","/api/works/"+work.ID+"/generate"),202,nil)
 v,_=a.db.work(work.ID)
 if v.Status!="image_pending"||len(v.Candidates)!=0||v.ImageAttempts!=2 {t.Fatalf("retry state: %+v",v)}
 mustJSON(t,postFile(t,mux,"/api/admin/works/"+work.ID+"/asset?kind=candidate","",true,pic.Bytes(),"image.png"),200,nil)
 mustJSON(t,req("POST","/api/works/"+work.ID+"/generate"),409,nil) // quota exhausted
 r:=httptest.NewRequest("POST","/api/works/"+work.ID+"/select",strings.NewReader(`{"index":0}`))
 r.Header.Set("Authorization","Bearer "+token)
 w:=httptest.NewRecorder();mux.ServeHTTP(w,r);mustJSON(t,w,202,nil)
 v,_=a.db.work(work.ID)
 if v.Status!="model_pending"||v.Selected!=0 {t.Fatalf("select single image: %+v",v)}
}


// Test model uploads larger than the former 12 MiB request limit.
// Verify every byte survives and oversize uploads return HTTP 413.
func TestModelUploadOver12MiBAndLimits(t *testing.T) {
 cfg := Config{DataDir:t.TempDir(),PublicURL:"http://localhost:8080",AdminToken:"admin-secret",SessionKey:"test-secret-longer-than-24-chars",Dev:true}
 if err:=os.MkdirAll(filepath.Join(cfg.DataDir,"media"),0700);err!=nil{t.Fatal(err)}
 db,err:=newStore(filepath.Join(cfg.DataDir,"store.json"));if err!=nil{t.Fatal(err)}
 a:=&App{cfg:cfg,db:db};mux:=a.routes()
 work:=&Work{ID:newID(),Owner:"test-owner",Original:"orig.jpg",Candidates:[]string{"candidate.png"},Selected:0,Status:"model_pending",Preview:[]string{}}
 if err:=db.update(func(d *Data) error {d.Works[work.ID]=work;return nil});err!=nil{t.Fatal(err)}
 glb:=make([]byte,13<<20)
 copy(glb[:4],[]byte("glTF"))
 glb[len(glb)-1]=0xAB
 got:=postFile(t,mux,"/api/admin/works/"+work.ID+"/asset?kind=model","",true,glb,"model.glb")
 mustJSON(t,got,200,nil)
 stored,_:=db.work(work.ID)
 if stored.Model==""{t.Fatal("model not saved")}
 saved,err:=os.ReadFile(filepath.Join(cfg.DataDir,"media",stored.Model))
 if err!=nil{t.Fatal(err)}
 if !bytes.Equal(glb,saved) {t.Fatalf("GLB changed or truncated: saved=%d original=%d",len(saved),len(glb))}
 // Images are still capped at 10 MiB.
 hugeImage:=make([]byte,11<<20)
 copy(hugeImage[:8],[]byte{137,80,78,71,13,10,26,10})
 imageResp:=postFile(t,mux,"/api/admin/works/"+work.ID+"/asset?kind=preview","",true,hugeImage,"large.png")
 mustJSON(t,imageResp,413,nil)
 // Content-length preflight rejects a model over 100 MiB without reading its body.
 req:=httptest.NewRequest("POST","/api/admin/works/"+work.ID+"/asset?kind=model",strings.NewReader(""))
 req.ContentLength=102<<20
 req.Header.Set("X-Admin-Token","admin-secret")
 record:=httptest.NewRecorder()
 mux.ServeHTTP(record,req)
 mustJSON(t,record,413,nil)
}


func TestReplacingGLBInvalidatesOldPreviews(t *testing.T) {
 cfg:=Config{DataDir:t.TempDir(), PublicURL:"http://localhost:8080", AdminToken:"admin-secret", SessionKey:"test-secret-longer-than-24-chars", Dev:true}
 if err:=os.MkdirAll(filepath.Join(cfg.DataDir,"media"),0700);err!=nil{t.Fatal(err)}
 db,err:=newStore(filepath.Join(cfg.DataDir,"store.json"));if err!=nil{t.Fatal(err)}
 work:=&Work{ID:newID(),Owner:"parent",Status:"model_ready",Original:"original.png",Candidates:[]string{"image.png"},Selected:0,Model:"old.glb",Preview:[]string{"old.png"}}
 if err:=db.update(func(d *Data) error{d.Works[work.ID]=work;return nil});err!=nil{t.Fatal(err)}
 a:=&App{cfg:cfg,db:db}
 glb:=append([]byte("glTF"),make([]byte,24)...)
 w:=postFile(t,a.routes(),"/api/admin/works/"+work.ID+"/asset?kind=model","",true,glb,"new.glb")
 mustJSON(t,w,200,nil)
 updated,_:=db.work(work.ID)
 if updated.Status!="model_ready"||updated.Model==""||updated.Model=="old.glb"||len(updated.Preview)!=0{
  t.Fatalf("model replacement kept stale preview or incorrect status: %+v",updated)
 }
 pic:=new(bytes.Buffer)
 if err:=png.Encode(pic,image.NewRGBA(image.Rect(0,0,8,8)));err!=nil{t.Fatal(err)}
 mustJSON(t,postFile(t,a.routes(),"/api/admin/works/"+work.ID+"/asset?kind=preview","",true,pic.Bytes(),"front.png"),200,nil)
 updated,_=db.work(work.ID)
 if updated.Status!="model_ready"||len(updated.Preview)!=1{t.Fatalf("new preview did not restore readiness: %+v",updated)}
}

func TestAdminBundledPreviewScripts(t *testing.T) {
 a:=&App{}
 for _,path:=range []string{"/admin/vendor/preview.mjs","/admin/vendor/three.module.min.js","/admin/vendor/loaders/GLTFLoader.js","/admin/vendor/utils/BufferGeometryUtils.js"}{
  req:=httptest.NewRequest("GET",path,nil)
  recorder:=httptest.NewRecorder()
  a.routes().ServeHTTP(recorder,req)
  if recorder.Code!=200{t.Fatalf("%s HTTP %d",path,recorder.Code)}
  if !strings.Contains(recorder.Header().Get("Content-Type"),"javascript"){t.Fatalf("%s wrong MIME: %s",path,recorder.Header().Get("Content-Type"))}
 }
}

func TestMigrateLegacyGLBWorks(t *testing.T) {
 db,err:=newStore(filepath.Join(t.TempDir(),"works.json"))
 if err!=nil{t.Fatal(err)}
 if err=db.update(func(data *Data)error {
  data.Works["legacy"]=&Work{ID:"legacy",Status:"model_pending",Model:"old.glb",Preview:[]string{}}
  data.Works["waiting"]=&Work{ID:"waiting",Status:"model_pending",Model:""}
  return nil
 });err!=nil{t.Fatal(err)}
 if err=activateLegacyGLBWorks(db);err!=nil{t.Fatal(err)}
 a,_:=db.work("legacy")
 b,_:=db.work("waiting")
 if a.Status!="model_ready" || b.Status!="model_pending" {t.Fatalf("migration changed unexpected states: %+v %+v",a,b)}
 if err=activateLegacyGLBWorks(db);err!=nil{t.Fatal(err)}
}

func TestInteractiveViewerAvailable(t *testing.T) {
 cfg:=Config{}
 a:=&App{cfg:cfg}
 req:=httptest.NewRequest("GET","/admin/3d/",nil)
 w:=httptest.NewRecorder()
 a.routes().ServeHTTP(w,req)
 if w.Code!=200 {t.Fatalf("viewer route HTTP %d: %s",w.Code,w.Body.String())}
 html:=w.Body.String()
 for _,expected:=range []string{"GLTFLoader","camera","model","旋转"} {
  if !strings.Contains(html,expected){t.Fatalf("viewer missing %s",expected)}
 }
}

func TestDBDialectBindings(t *testing.T) {
	if got := (&SQLStore{dialect: "postgres"}).bind("SELECT * FROM ty_orders WHERE id=? AND owner_id=?"); got != "SELECT * FROM ty_orders WHERE id=$1 AND owner_id=$2" {
		t.Fatalf("PostgreSQL binding failed: %s", got)
	}
	if got := (&SQLStore{dialect: "mysql"}).bind("SELECT * FROM ty_orders WHERE id=?"); got != "SELECT * FROM ty_orders WHERE id=?" {
		t.Fatalf("MySQL binding failed: %s", got)
	}
}

func TestRepositoryConfiguration(t *testing.T) {
	if _, err := openRepository(Config{DBDriver: "file", Dev: false, DataDir: t.TempDir()}); err == nil {
		t.Fatal("production must not use JSON storage")
	}
	if _, err := openRepository(Config{DBDriver: "postgres"}); err == nil {
		t.Fatal("postgres without a DSN should fail")
	}
	if _, err := openRepository(Config{DBDriver: "mysql"}); err == nil {
		t.Fatal("mysql without a DSN should fail")
	}
	file, err := openRepository(Config{DBDriver: "file", Dev: true, DataDir: t.TempDir()})
	if err != nil {
		t.Fatal(err)
	}
	_ = file.Close()
}

// Run against each engine by setting TEST_DB_DRIVER and TEST_DATABASE_URL.
// It touches only records it created and cleans them up afterward.
func TestSQLRepositoryRoundTrip(t *testing.T) {
	dialect := os.Getenv("TEST_DB_DRIVER")
	dsn := os.Getenv("TEST_DATABASE_URL")
	if dialect == "" || dsn == "" {
		t.Skip("set TEST_DB_DRIVER and TEST_DATABASE_URL for real PostgreSQL/MySQL integration test")
	}
	s, err := newSQLStore(dialect, dsn)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	id, oid, owner := newID(), newID(), "integration-"+newID()
	defer func() {
		if err := s.update(func(d *Data) error {
			delete(d.Works, id)
			delete(d.Orders, oid)
			return nil
		}); err != nil {
			t.Errorf("cleanup: %v", err)
		}
	}()
	work := &Work{ID: id, Owner: owner, CreatedAt: now(), Original: "drawing.png", Selected: -1, Status: "image_pending"}
	order := &Order{ID: oid, Owner: owner, WorkID: id, CreatedAt: now(), Name: "测试", Phone: "13800138000", Address: "广东省广州市测试地址", PriceFen: 13900, Status: "awaiting_payment"}
	if err := s.update(func(d *Data) error { d.Works[id] = work; d.Orders[oid] = order; return nil }); err != nil {
		t.Fatal(err)
	}
	if got, ok := s.work(id); !ok || got.Owner != owner {
		t.Fatalf("lost work: %#v", got)
	}
	if got, ok := s.order(oid); !ok || got.PriceFen != 13900 {
		t.Fatalf("lost order: %#v", got)
	}
	if len(s.works(owner, false)) != 1 || len(s.orders(owner, false)) != 1 {
		t.Fatal("owner filters did not work")
	}
	if err := s.update(func(d *Data) error { d.Orders[oid].Status = "paid"; return nil }); err != nil {
		t.Fatal(err)
	}
	reopened, err := newSQLStore(dialect, dsn)
	if err != nil {
		t.Fatal(err)
	}
	defer reopened.Close()
	if got, ok := reopened.order(oid); !ok || got.Status != "paid" {
		t.Fatalf("transaction did not persist: %#v", got)
	}
}
