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
	for i := 0; i < 2; i++ {
		w := postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=candidate", "", true, pngbuf.Bytes(), "candidate.png")
		mustJSON(t, w, 200, nil)
	}
	var ready Work
	mustJSON(t, makeReq("GET", "/api/works/"+work.ID, "", false), 200, &ready)
	if ready.Status != "image_ready" || len(ready.Candidates) != 2 {
		t.Fatal("image stage failed")
	}
	mustJSON(t, makeReq("POST", "/api/works/"+work.ID+"/select", `{"index":1}`, false), 202, nil)
	glb := append([]byte("glTF"), make([]byte, 16)...)
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=model", "", true, glb, "model.glb"), 200, nil)
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=preview", "", true, pngbuf.Bytes(), "preview.png"), 200, nil)
	mustJSON(t, makeReq("GET", "/api/works/"+work.ID, "", false), 200, &ready)
	if ready.Status != "model_ready" {
		t.Fatalf("model stage failed: %s", ready.Status)
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
