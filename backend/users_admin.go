package main

import (
	"errors"
	"fmt"
	"net/http"
	"sort"
	"strings"
)

// 运营用户模块：用户档案、统计、账号状态与管理操作日志。
func backfillLegacyUsers(db Repository) error {
	owners := map[string]bool{}
	for _, w := range db.works("", true) {
		if w.Owner != "" {
			owners[w.Owner] = true
		}
	}
	for _, o := range db.orders("", true) {
		if o.Owner != "" {
			owners[o.Owner] = true
		}
	}
	for owner := range owners {
		if _, ok := db.user(owner); ok {
			delete(owners, owner)
		}
	}
	if len(owners) == 0 {
		return nil
	}
	return db.update(func(d *Data) error {
		if d.Users == nil {
			d.Users = map[string]*User{}
		}
		for owner := range owners {
			if _, ok := d.Users[owner]; !ok {
				d.Users[owner] = &User{ID: newID(), Owner: owner, CreatedAt: now()}
			}
		}
		return nil
	})
}

func (a *App) adminDashboard(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	works := a.db.works("", true)
	orders := a.db.orders("", true)
	metrics := map[string]int{"users": 0, "works": len(works), "orders": len(orders), "image_pending": 0, "model_pending": 0, "awaiting_payment": 0, "paid": 0, "printing": 0, "shipped": 0, "completed": 0, "refund_requested": 0}
	for _, v := range works {
		if _, ok := metrics[v.Status]; ok {
			metrics[v.Status]++
		}
	}
	revenue := 0
	for _, v := range orders {
		if _, ok := metrics[v.Status]; ok {
			metrics[v.Status]++
		}
		if v.RefundStatus == "requested" {
			metrics["refund_requested"]++
		}
		if v.Status != "awaiting_payment" && v.Status != "canceled" {
			revenue += v.PriceFen
		}
	}
	_ = a.db.update(func(d *Data) error { metrics["users"] = len(d.Users); return nil })
	reply(w, 200, map[string]any{"metrics": metrics, "paid_gross_fen": revenue, "pay_configured": a.payReady(), "tracking_configured": a.cfg.KuaidiKey != "" && a.cfg.KuaidiCustomer != ""})
}

func (a *App) adminUsers(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	var list []User
	err := a.db.update(func(d *Data) error {
		for _, u := range d.Users {
			list = append(list, *u)
		}
		return nil
	})
	if err != nil {
		fail(w, 500, "用户列表加载失败")
		return
	}
	keyword := strings.ToLower(strings.TrimSpace(r.URL.Query().Get("q")))
	works := a.db.works("", true)
	orders := a.db.orders("", true)
	result := []map[string]any{}
	for _, u := range list {
		if keyword != "" && !strings.Contains(strings.ToLower(u.ID), keyword) {
			continue
		}
		wc, oc := 0, 0
		for _, v := range works {
			if v.Owner == u.Owner {
				wc++
			}
		}
		for _, v := range orders {
			if v.Owner == u.Owner {
				oc++
			}
		}
		result = append(result, map[string]any{"id": u.ID, "created_at": u.CreatedAt, "last_login": u.LastLogin, "disabled": u.Disabled, "works": wc, "orders": oc})
	}
	sort.Slice(result, func(i, j int) bool { return result[i]["created_at"].(string) > result[j]["created_at"].(string) })
	reply(w, 200, result)
}

func (a *App) adminUserStatus(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	var q struct {
		Disabled bool `json:"disabled"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数错误")
		return
	}
	id := r.PathValue("id")
	err := a.db.update(func(d *Data) error {
		for _, u := range d.Users {
			if u.ID == id {
				u.Disabled = q.Disabled
				actor, _, _ := a.adminIdentity(r)
				recordAudit(d, actor, "user.status", id, fmt.Sprintf("disabled=%v", q.Disabled))
				return nil
			}
		}
		return errNotFound
	})
	if errors.Is(err, errNotFound) {
		fail(w, 404, "用户不存在")
		return
	}
	if err != nil {
		fail(w, 500, "修改失败")
		return
	}
	reply(w, 200, map[string]string{"status": "ok"})
}

func (a *App) adminAudit(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	items := []*AuditRecord{}
	err := a.db.update(func(d *Data) error {
		for _, v := range d.Audit {
			x := *v
			items = append(items, &x)
		}
		return nil
	})
	if err != nil {
		fail(w, 500, "加载失败")
		return
	}
	sort.Slice(items, func(i, j int) bool { return items[i].CreatedAt > items[j].CreatedAt })
	if len(items) > 300 {
		items = items[:300]
	}
	reply(w, 200, items)
}
