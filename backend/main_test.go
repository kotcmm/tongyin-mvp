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

func TestShippingAddressServerPersistenceAndUserIsolation(t *testing.T) {
	cfg := Config{DataDir: t.TempDir(), SessionKey: "test-secret-longer-than-24-chars", PriceFen: 9900, PublicURL: "http://localhost:18080", Dev: true}
	store, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	app := &App{cfg: cfg, db: store}
	mux := app.routes()
	alice := app.token("alice-openid")
	bob := app.token("bob-openid")
	call := func(method, path, token, body string) *httptest.ResponseRecorder {
		req := httptest.NewRequest(method, path, strings.NewReader(body))
		req.Header.Set("Content-Type", "application/json")
		if token != "" {
			req.Header.Set("Authorization", "Bearer "+token)
		}
		rec := httptest.NewRecorder()
		mux.ServeHTTP(rec, req)
		return rec
	}
	mustJSON(t, call("GET", "/api/shipping-address", "", ""), 401, nil)
	var empty struct {
		Address *ShippingAddress `json:"address"`
	}
	mustJSON(t, call("GET", "/api/shipping-address", alice, ""), 200, &empty)
	if empty.Address != nil {
		t.Fatalf("new user should have no saved address: %+v", empty)
	}
	address1 := `{"name":"张小明","phone":"13800138000","address":"广东省广州市海珠区测试路100号"}`
	mustJSON(t, call("PUT", "/api/shipping-address", alice, address1), 200, nil)
	var got struct {
		Address *ShippingAddress `json:"address"`
	}
	mustJSON(t, call("GET", "/api/shipping-address", alice, ""), 200, &got)
	if got.Address == nil || got.Address.Name != "张小明" || got.Address.Owner != "" {
		t.Fatalf("saved address incorrect: %+v", got)
	}
	mustJSON(t, call("GET", "/api/shipping-address", bob, ""), 200, &empty)
	if empty.Address != nil {
		t.Fatal("one user read another user's address")
	}
	mustJSON(t, call("PUT", "/api/shipping-address", alice, `{"name":"A","phone":"1","address":"short"}`), 400, nil)
	work := &Work{ID: "order-work", Owner: "alice-openid", Status: "model_ready", Model: "valid.glb"}
	if err := store.update(func(d *Data) error { d.Works[work.ID] = work; return nil }); err != nil {
		t.Fatal(err)
	}
	var order Order
	address2 := `{"work_id":"order-work","name":"李小红","phone":"13900139000","address":"广东省广州市天河区测试新路200号"}`
	mustJSON(t, call("POST", "/api/orders", alice, address2), 201, &order)
	if order.Name != "李小红" || order.Owner != "" {
		t.Fatalf("order snapshot invalid: %+v", order)
	}
	mustJSON(t, call("GET", "/api/shipping-address", alice, ""), 200, &got)
	if got.Address.Name != "李小红" {
		t.Fatalf("checkout did not update saved default: %+v", got)
	}
	mustJSON(t, call("PUT", "/api/shipping-address", alice, address1), 200, nil)
	savedOrder, ok := store.order(order.ID)
	if !ok || savedOrder.Name != "李小红" {
		t.Fatal("changing default must not modify existing order")
	}
	reloaded, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	persisted, ok := reloaded.address("alice-openid")
	if !ok || persisted.Name != "张小明" {
		t.Fatalf("address not persisted on server: %+v", persisted)
	}
	_, leaked := reloaded.address("bob-openid")
	if leaked {
		t.Fatal("other user has unrelated address")
	}
}

func TestLegacyOrdersRestoreLastAddress(t *testing.T) {
	cfg := Config{DataDir: t.TempDir(), SessionKey: "test-secret-longer-than-24-chars"}
	store, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	app := &App{db: store, cfg: cfg}
	if err := store.update(func(d *Data) error {
		d.Orders["old"] = &Order{ID: "old", Owner: "alice", Name: "张三", Phone: "13800138000", Address: "广州市天河区测试路10号", CreatedAt: "2026-01-01T00:00:00+08:00"}
		d.Orders["new"] = &Order{ID: "new", Owner: "alice", Name: "李四", Phone: "13900139000", Address: "广州市海珠区测试路20号", CreatedAt: "2026-02-01T00:00:00+08:00"}
		d.Orders["foreign"] = &Order{ID: "foreign", Owner: "bob", Name: "其他", Phone: "13600136000", Address: "上海市徐汇区测试路30号", CreatedAt: "2026-03-01T00:00:00+08:00"}
		return nil
	}); err != nil {
		t.Fatal(err)
	}
	a := app.lastShippingAddress("alice")
	if a == nil || a.Name != "李四" {
		t.Fatalf("legacy address recovery failed: %+v", a)
	}
	if app.lastShippingAddress("unknown") != nil {
		t.Fatal("other user legacy address leaked")
	}
}

func TestOneImageAndFreeRetryLimit(t *testing.T) {
	cfg := Config{DataDir: t.TempDir(), PublicURL: "http://localhost:8080", AdminToken: "admin-secret", SessionKey: "test-secret-longer-than-24-chars", Dev: true, FreeImageAttempts: 2}
	_ = os.MkdirAll(filepath.Join(cfg.DataDir, "media"), 0700)
	db, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	a := &App{cfg: cfg, db: db}
	mux := a.routes()
	token := a.token("test-owner")
	pic := new(bytes.Buffer)
	_ = png.Encode(pic, image.NewRGBA(image.Rect(0, 0, 4, 4)))
	uploaded := postFile(t, mux, "/api/works", token, false, pic.Bytes(), "drawing.png")
	var work Work
	mustJSON(t, uploaded, 201, &work)
	req := func(method, path string) *httptest.ResponseRecorder {
		r := httptest.NewRequest(method, path, strings.NewReader("{}"))
		r.Header.Set("Authorization", "Bearer "+token)
		w := httptest.NewRecorder()
		mux.ServeHTTP(w, r)
		return w
	}
	// No model may be uploaded before selecting the sole image.
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=model", "", true, append([]byte("glTF"), make([]byte, 12)...), "x.glb"), 409, nil)
	mustJSON(t, req("POST", "/api/works/"+work.ID+"/generate"), 202, nil)
	mustJSON(t, req("POST", "/api/works/"+work.ID+"/generate"), 409, nil) // pending task is not duplicated
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=candidate", "", true, pic.Bytes(), "image.png"), 200, nil)
	v, ok := a.db.work(work.ID)
	if !ok || v.Status != "image_ready" || len(v.Candidates) != 1 || v.ImageAttempts != 1 {
		t.Fatalf("first image: %+v", v)
	}
	mustJSON(t, req("POST", "/api/works/"+work.ID+"/generate"), 202, nil)
	v, _ = a.db.work(work.ID)
	if v.Status != "image_pending" || len(v.Candidates) != 0 || v.ImageAttempts != 2 {
		t.Fatalf("retry state: %+v", v)
	}
	mustJSON(t, postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=candidate", "", true, pic.Bytes(), "image.png"), 200, nil)
	mustJSON(t, req("POST", "/api/works/"+work.ID+"/generate"), 409, nil) // quota exhausted
	r := httptest.NewRequest("POST", "/api/works/"+work.ID+"/select", strings.NewReader(`{"index":0}`))
	r.Header.Set("Authorization", "Bearer "+token)
	w := httptest.NewRecorder()
	mux.ServeHTTP(w, r)
	mustJSON(t, w, 202, nil)
	v, _ = a.db.work(work.ID)
	if v.Status != "model_pending" || v.Selected != 0 {
		t.Fatalf("select single image: %+v", v)
	}
}

// Test model uploads larger than the former 12 MiB request limit.
// Verify every byte survives and oversize uploads return HTTP 413.
func TestModelUploadOver12MiBAndLimits(t *testing.T) {
	cfg := Config{DataDir: t.TempDir(), PublicURL: "http://localhost:8080", AdminToken: "admin-secret", SessionKey: "test-secret-longer-than-24-chars", Dev: true}
	if err := os.MkdirAll(filepath.Join(cfg.DataDir, "media"), 0700); err != nil {
		t.Fatal(err)
	}
	db, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	a := &App{cfg: cfg, db: db}
	mux := a.routes()
	work := &Work{ID: newID(), Owner: "test-owner", Original: "orig.jpg", Candidates: []string{"candidate.png"}, Selected: 0, Status: "model_pending", Preview: []string{}}
	if err := db.update(func(d *Data) error { d.Works[work.ID] = work; return nil }); err != nil {
		t.Fatal(err)
	}
	glb := make([]byte, 13<<20)
	copy(glb[:4], []byte("glTF"))
	glb[len(glb)-1] = 0xAB
	got := postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=model", "", true, glb, "model.glb")
	mustJSON(t, got, 200, nil)
	stored, _ := db.work(work.ID)
	if stored.Model == "" {
		t.Fatal("model not saved")
	}
	saved, err := os.ReadFile(filepath.Join(cfg.DataDir, "media", stored.Model))
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(glb, saved) {
		t.Fatalf("GLB changed or truncated: saved=%d original=%d", len(saved), len(glb))
	}
	// Images are still capped at 10 MiB.
	hugeImage := make([]byte, 11<<20)
	copy(hugeImage[:8], []byte{137, 80, 78, 71, 13, 10, 26, 10})
	imageResp := postFile(t, mux, "/api/admin/works/"+work.ID+"/asset?kind=preview", "", true, hugeImage, "large.png")
	mustJSON(t, imageResp, 413, nil)
	// Content-length preflight rejects a model over 100 MiB without reading its body.
	req := httptest.NewRequest("POST", "/api/admin/works/"+work.ID+"/asset?kind=model", strings.NewReader(""))
	req.ContentLength = 102 << 20
	req.Header.Set("X-Admin-Token", "admin-secret")
	record := httptest.NewRecorder()
	mux.ServeHTTP(record, req)
	mustJSON(t, record, 413, nil)
}

func TestReplacingGLBInvalidatesOldPreviews(t *testing.T) {
	cfg := Config{DataDir: t.TempDir(), PublicURL: "http://localhost:8080", AdminToken: "admin-secret", SessionKey: "test-secret-longer-than-24-chars", Dev: true}
	if err := os.MkdirAll(filepath.Join(cfg.DataDir, "media"), 0700); err != nil {
		t.Fatal(err)
	}
	db, err := newStore(filepath.Join(cfg.DataDir, "store.json"))
	if err != nil {
		t.Fatal(err)
	}
	work := &Work{ID: newID(), Owner: "parent", Status: "model_ready", Original: "original.png", Candidates: []string{"image.png"}, Selected: 0, Model: "old.glb", Preview: []string{"old.png"}}
	if err := db.update(func(d *Data) error { d.Works[work.ID] = work; return nil }); err != nil {
		t.Fatal(err)
	}
	a := &App{cfg: cfg, db: db}
	glb := append([]byte("glTF"), make([]byte, 24)...)
	w := postFile(t, a.routes(), "/api/admin/works/"+work.ID+"/asset?kind=model", "", true, glb, "new.glb")
	mustJSON(t, w, 200, nil)
	updated, _ := db.work(work.ID)
	if updated.Status != "model_ready" || updated.Model == "" || updated.Model == "old.glb" || len(updated.Preview) != 0 {
		t.Fatalf("model replacement kept stale preview or incorrect status: %+v", updated)
	}
	pic := new(bytes.Buffer)
	if err := png.Encode(pic, image.NewRGBA(image.Rect(0, 0, 8, 8))); err != nil {
		t.Fatal(err)
	}
	mustJSON(t, postFile(t, a.routes(), "/api/admin/works/"+work.ID+"/asset?kind=preview", "", true, pic.Bytes(), "front.png"), 200, nil)
	updated, _ = db.work(work.ID)
	if updated.Status != "model_ready" || len(updated.Preview) != 1 {
		t.Fatalf("new preview did not restore readiness: %+v", updated)
	}
}

func TestAdminBundledPreviewScripts(t *testing.T) {
	a := &App{}
	for _, path := range []string{"/admin/vendor/preview.mjs", "/admin/vendor/three.module.min.js", "/admin/vendor/loaders/GLTFLoader.js", "/admin/vendor/utils/BufferGeometryUtils.js"} {
		req := httptest.NewRequest("GET", path, nil)
		recorder := httptest.NewRecorder()
		a.routes().ServeHTTP(recorder, req)
		if recorder.Code != 200 {
			t.Fatalf("%s HTTP %d", path, recorder.Code)
		}
		if !strings.Contains(recorder.Header().Get("Content-Type"), "javascript") {
			t.Fatalf("%s wrong MIME: %s", path, recorder.Header().Get("Content-Type"))
		}
	}
}

func TestMigrateLegacyGLBWorks(t *testing.T) {
	db, err := newStore(filepath.Join(t.TempDir(), "works.json"))
	if err != nil {
		t.Fatal(err)
	}
	if err = db.update(func(data *Data) error {
		data.Works["legacy"] = &Work{ID: "legacy", Status: "model_pending", Model: "old.glb", Preview: []string{}}
		data.Works["waiting"] = &Work{ID: "waiting", Status: "model_pending", Model: ""}
		return nil
	}); err != nil {
		t.Fatal(err)
	}
	if err = activateLegacyGLBWorks(db); err != nil {
		t.Fatal(err)
	}
	a, _ := db.work("legacy")
	b, _ := db.work("waiting")
	if a.Status != "model_ready" || b.Status != "model_pending" {
		t.Fatalf("migration changed unexpected states: %+v %+v", a, b)
	}
	if err = activateLegacyGLBWorks(db); err != nil {
		t.Fatal(err)
	}
}

func TestInteractiveViewerAvailable(t *testing.T) {
	cfg := Config{}
	a := &App{cfg: cfg}
	req := httptest.NewRequest("GET", "/admin/3d/", nil)
	w := httptest.NewRecorder()
	a.routes().ServeHTTP(w, req)
	if w.Code != 200 {
		t.Fatalf("viewer route HTTP %d: %s", w.Code, w.Body.String())
	}
	html := w.Body.String()
	for _, expected := range []string{"GLTFLoader", "camera", "model", "旋转"} {
		if !strings.Contains(html, expected) {
			t.Fatalf("viewer missing %s", expected)
		}
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
