package main

import (
	"encoding/json"
	"net/http"
)

// HTTP 辅助层：统一响应、参数解码与身份校验。
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
	if err := a.ensureUser(v); err != nil {
		fail(w, 403, err.Error())
		return ""
	}
	return v
}

func (a *App) admin(w http.ResponseWriter, r *http.Request) bool {
	_, role, ok := a.adminIdentity(r)
	if !ok {
		fail(w, 401, "管理员未登录或会话已失效")
		return false
	}
	if !adminAllowed(role, r) {
		fail(w, 403, "当前角色无权进行此操作")
		return false
	}
	return true
}
