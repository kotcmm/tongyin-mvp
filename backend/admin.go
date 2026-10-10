package main

import (
	"errors"
	"fmt"
	"net/http"
	"strings"
)

func (a *App) adminWorks(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	out := []*Work{}
	for _, wk := range a.db.works("", true) {
		out = append(out, a.exposeWork(wk))
	}
	reply(w, 200, out)
}
func (a *App) adminOrders(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	out := a.db.orders("", true)
	reply(w, 200, out)
}
func (a *App) adminAsset(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	id := r.PathValue("id")
	if _, ok := a.db.work(id); !ok {
		fail(w, 404, "作品不存在")
		return
	}
	kind := r.URL.Query().Get("kind")
	if kind != "candidate" && kind != "preview" && kind != "model" {
		fail(w, 400, "kind 必须是 candidate、preview 或 model")
		return
	}
	name, err := a.upload(r, kind != "model")
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	err = a.db.update(func(d *Data) error {
		wk, ok := d.Works[id]
		if !ok {
			return errNotFound
		}
		switch kind {
		case "candidate":
			if len(wk.Candidates) >= 2 {
				return errors.New("当前最多支持两张候选图")
			}
			wk.Candidates = append(wk.Candidates, name)
			if len(wk.Candidates) == 2 {
				wk.Status = "image_ready"
			}
		case "preview":
			if len(wk.Preview) >= 4 {
				return errors.New("最多上传4张预览图")
			}
			wk.Preview = append(wk.Preview, name)
			if wk.Model != "" {
				wk.Status = "model_ready"
			}
		case "model":
			wk.Model = name
			if len(wk.Preview) > 0 {
				wk.Status = "model_ready"
			}
		}
		return nil
	})
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	wk, _ := a.db.work(id)
	reply(w, 200, a.exposeWork(wk))
}
func (a *App) adminWorkStatus(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	id := r.PathValue("id")
	var q struct{ Status, Note string }
	if decode(r, &q) != nil {
		fail(w, 400, "参数无效")
		return
	}
	if q.Status != "failed" && q.Status != "image_pending" && q.Status != "model_pending" && q.Status != "model_ready" {
		fail(w, 400, "状态无效")
		return
	}
	err := a.db.update(func(d *Data) error {
		wk, ok := d.Works[id]
		if !ok {
			return errNotFound
		}
		if q.Status == "model_ready" && (wk.Model == "" || len(wk.Preview) == 0) {
			return fmt.Errorf("缺少模型或预览图")
		}
		wk.Status = q.Status
		wk.Note = strings.TrimSpace(q.Note)
		return nil
	})
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	reply(w, 200, map[string]string{"status": "ok"})
}
func (a *App) adminOrderStatus(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	id := r.PathValue("id")
	var q struct{ Status, Tracking string }
	if decode(r, &q) != nil {
		fail(w, 400, "参数无效")
		return
	}
	valid := map[string]bool{"printing": true, "shipped": true, "completed": true, "canceled": true}
	if !valid[q.Status] {
		fail(w, 400, "状态无效")
		return
	}
	err := a.db.update(func(d *Data) error {
		o, ok := d.Orders[id]
		if !ok {
			return errNotFound
		}
		if o.Status == "awaiting_payment" && q.Status != "canceled" {
			return errors.New("订单尚未支付，不可制作")
		}
		if o.Status == "canceled" || o.Status == "completed" {
			return errors.New("订单已结束")
		}
		if q.Status == "canceled" && o.Status != "awaiting_payment" {
			return errors.New("已支付订单不能直接取消，请先执行退款流程")
		}
		next := map[string]string{"paid": "printing", "printing": "shipped", "shipped": "completed"}
		if q.Status != "canceled" && next[o.Status] != q.Status {
			return errors.New("不允许跳过订单状态")
		}
		if q.Status == "shipped" && strings.TrimSpace(q.Tracking) == "" {
			return errors.New("请填写快递单号")
		}
		o.Status = q.Status
		if q.Tracking != "" {
			o.Tracking = strings.TrimSpace(q.Tracking)
		}
		return nil
	})
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	reply(w, 200, map[string]string{"status": "ok"})
}
