package main

import (
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// main.go 仅负责应用启动、依赖初始化和路由装配；业务处理分别放入对应模块。
type App struct {
	cfg    Config
	db     Repository
	client *http.Client
}

func main() {
	c := config()
	if len(c.SessionKey) < 24 {
		log.Fatal("SESSION_SECRET 至少需要24位")
	}
	if c.AdminToken == "" && c.AdminPassword == "" {
		log.Print("提示：没有提供 ADMIN_TOKEN 或 ADMIN_BOOTSTRAP_PASSWORD；需要数据库中已有可用管理员才能登录")
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
	if err := bootstrapAdmin(db, c); err != nil {
		log.Fatal(err)
	}
	if err := checkAdminReady(db, c); err != nil {
		log.Fatal(err)
	}
	if err := backfillLegacyUsers(db); err != nil {
		log.Fatal(err)
	}
	if err := activateLegacyGLBWorks(db); err != nil {
		log.Fatal(err)
	}
	app := &App{cfg: c, db: db, client: &http.Client{Timeout: 90 * time.Second}}
	s := &http.Server{Addr: c.Addr, Handler: app.routes(), ReadHeaderTimeout: 10 * time.Second, MaxHeaderBytes: 1 << 20}
	if c.AIWorkerURL == "" {
		log.Print("提示：未配置旧版 AI_WORKER_URL；可在后台 AI 配置页启用生成模型")
	}
	log.Printf("童印 API listening on %s", c.Addr)
	log.Fatal(s.ListenAndServe())
}

// Older versions required GLB + preview images to become ready.
// When upgrading, make existing uploaded GLBs immediately viewable.

func activateLegacyGLBWorks(db Repository) error {
	needMigration := false
	for _, work := range db.works("", true) {
		if work.Status == "model_pending" && work.Model != "" {
			needMigration = true
			break
		}
	}
	if !needMigration {
		return nil
	}
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
	mux.HandleFunc("GET /api/me", a.customerMe)
	mux.HandleFunc("POST /api/admin/auth/login", a.adminLogin)
	mux.HandleFunc("GET /api/admin/me", a.adminMe)
	mux.HandleFunc("GET /api/admin/dashboard", a.adminDashboard)
	mux.HandleFunc("GET /api/admin/users", a.adminUsers)
	mux.HandleFunc("POST /api/admin/users/{id}/status", a.adminUserStatus)
	mux.HandleFunc("GET /api/admin/audit", a.adminAudit)
	mux.HandleFunc("GET /api/admin/accounts", a.adminAccounts)
	mux.HandleFunc("POST /api/admin/accounts", a.adminAccounts)
	mux.HandleFunc("PUT /api/admin/accounts/{id}", a.adminAccountUpdate)
	mux.HandleFunc("GET /api/config", func(w http.ResponseWriter, r *http.Request) {
		reply(w, 200, map[string]any{"price_fen": a.cfg.PriceFen, "dev": a.cfg.Dev, "ai_auto_enabled": a.aiAutoEnabled(), "free_image_attempts": a.imageLimit()})
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
	mux.HandleFunc("POST /api/orders/{id}/cancel", a.cancelOrder)
	mux.HandleFunc("GET /api/orders/{id}/tracking", a.orderTracking)
	mux.HandleFunc("POST /api/admin/orders/{id}/shipment", a.adminShipment)
	mux.HandleFunc("POST /api/admin/orders/{id}/tracking", a.adminTrackingEntry)
	mux.HandleFunc("POST /api/admin/orders/{id}/tracking/refresh", a.adminTrackingRefresh)
	mux.HandleFunc("POST /api/admin/orders/{id}/refund", a.adminRefundRequest)
	mux.HandleFunc("POST /api/pay/notify", a.payNotify)
	mux.HandleFunc("GET /api/admin/works", a.adminWorks)
	mux.HandleFunc("GET /api/admin/search/works", a.adminPagedWorks)
	mux.HandleFunc("GET /api/admin/search/orders", a.adminPagedOrders)
	mux.HandleFunc("GET /api/admin/search/users", a.adminPagedUsers)
	mux.HandleFunc("POST /api/admin/works/{id}/asset", a.adminAsset)
	mux.HandleFunc("POST /api/admin/works/{id}/status", a.adminWorkStatus)
	mux.HandleFunc("GET /api/admin/orders", a.adminOrders)
	mux.HandleFunc("POST /api/admin/orders/{id}/status", a.adminOrderStatus)
	mux.HandleFunc("GET /api/admin/ai/config", a.adminAIConfig)
	mux.HandleFunc("POST /api/admin/ai/providers", a.adminAISaveProvider)
	mux.HandleFunc("DELETE /api/admin/ai/providers/{id}", a.adminAIDeleteProvider)
	mux.HandleFunc("PUT /api/admin/ai/prompts/{stage}", a.adminAISavePrompt)
	mux.HandleFunc("POST /api/internal/ai-result", a.aiResult)
	mux.HandleFunc("GET /admin", func(w http.ResponseWriter, r *http.Request) { http.Redirect(w, r, "/admin/", 302) })
	mux.HandleFunc("GET /admin/{$}", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write(embeddedAdminHTML)
	})
	// 管理端静态文件直接内嵌在 Go 二进制中，不需要第二个部署进程。
	// 管理后台只有静态资源，复用同一个 Go 监听端口。
	mux.HandleFunc("GET /admin/core.js", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write(embeddedAdminCore)
	})
	mux.HandleFunc("GET /admin/views.js", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write(embeddedAdminViews)
	})
	mux.HandleFunc("GET /admin/ai-admin.js", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write(embeddedAdminAI)
	})
	mux.HandleFunc("GET /admin/actions.js", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write(embeddedAdminActions)
	})
	mux.HandleFunc("GET /admin/app.css", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/css; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write(embeddedAdminCSS)
	})

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
