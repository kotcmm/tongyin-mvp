package main

import (
	"os"
	"strconv"
	"strings"
)

type Config struct {
	Addr, DataDir, PublicURL, AdminToken, AppID, AppSecret, SessionKey, AIWorkerURL, AIWorkerToken, AIWebhookToken string
	MchID, MchSerial, MchPrivateKeyPath, PlatformPublicKeyPath, PlatformSerial, PayAPIKey, PayNotifyURL            string
	DBDriver, DatabaseURL                                                                                          string
	AdminUsername, AdminPassword                                                                                   string
	KuaidiKey, KuaidiCustomer                                                                                      string
	PriceFen, FreeImageAttempts                                                                                    int
	Dev                                                                                                            bool
}

func env(k, d string) string {
	if v := strings.TrimSpace(os.Getenv(k)); v != "" {
		return v
	}
	return d
}
func config() Config {
	price, _ := strconv.Atoi(env("PRODUCT_PRICE_FEN", "12900"))
	attempts, _ := strconv.Atoi(env("FREE_IMAGE_ATTEMPTS", "2"))
	if attempts < 1 {
		attempts = 2
	}
	if price <= 0 {
		price = 12900
	}
	return Config{AdminUsername: env("ADMIN_BOOTSTRAP_USER", "admin"), AdminPassword: os.Getenv("ADMIN_BOOTSTRAP_PASSWORD"), KuaidiKey: os.Getenv("KUAIDI100_KEY"), KuaidiCustomer: os.Getenv("KUAIDI100_CUSTOMER"), DBDriver: env("DB_DRIVER", "postgres"), DatabaseURL: env("DATABASE_URL", ""), Addr: env("LISTEN_ADDR", ":8080"), DataDir: env("DATA_DIR", "./data"), PublicURL: strings.TrimRight(env("PUBLIC_URL", "http://localhost:8080"), "/"), AdminToken: os.Getenv("ADMIN_TOKEN"), AppID: os.Getenv("WX_APP_ID"), AppSecret: os.Getenv("WX_APP_SECRET"), SessionKey: os.Getenv("SESSION_SECRET"), AIWorkerURL: os.Getenv("AI_WORKER_URL"), AIWorkerToken: os.Getenv("AI_WORKER_TOKEN"), AIWebhookToken: os.Getenv("AI_WEBHOOK_TOKEN"), MchID: os.Getenv("WX_MCH_ID"), MchSerial: os.Getenv("WX_MCH_SERIAL"), MchPrivateKeyPath: os.Getenv("WX_MCH_PRIVATE_KEY"), PlatformPublicKeyPath: os.Getenv("WX_PLATFORM_PUBLIC_KEY"), PlatformSerial: os.Getenv("WX_PLATFORM_SERIAL"), PayAPIKey: os.Getenv("WX_PAY_API_V3_KEY"), PayNotifyURL: os.Getenv("WX_PAY_NOTIFY_URL"), PriceFen: price, FreeImageAttempts: attempts, Dev: env("DEV_MODE", "false") == "true"}
}
