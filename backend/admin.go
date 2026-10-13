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
		item:=a.exposeWork(wk)
        if u,ok:=a.db.user(wk.Owner);ok{item.UserID=u.ID}
        out=append(out,item)
	}
	reply(w, 200, out)
}
func (a *App) adminOrders(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	out := a.db.orders("", true)
    for _,o:=range out{
      if u,ok:=a.db.user(o.Owner);ok{o.UserID=u.ID}
      o.Owner=""
    }
    reply(w,200,out)
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
	name, err := a.upload(w, r, kind != "model")
	if err != nil {
		uploadFailure(w, err)
		return
	}
	err = a.db.update(func(d *Data) error {
		wk, ok := d.Works[id]
		if !ok {
			return errNotFound
		}
		switch kind {
		case "candidate":
			if wk.Status != "image_pending" && wk.Status != "image_ready" && !(wk.Status == "failed" && wk.Selected < 0) {
				return errors.New("请先完成原画上传，当前阶段不可上传形象图")
			}
			wk.Candidates = []string{name} // one output per generation; admin may replace a result
			wk.Preview = []string{} // discard artifacts from an older / incorrect stage
			wk.Model = ""
			if wk.ImageAttempts == 0 { wk.ImageAttempts = 1 } // legacy/manual results also count as initial generation
			wk.Selected = -1
			wk.Status = "image_ready"
			wk.Note = ""
		case "preview":
			if wk.Status != "model_pending" && wk.Status != "model_ready" {
				return errors.New("请等待用户确认形象后，再上传白模预览图")
			}
			if len(wk.Preview) >= 4 { return errors.New("最多上传4张白模角度图") }
			wk.Preview = append(wk.Preview, name)
			if wk.Model != "" { wk.Status = "model_ready" }
		case "model":
            if wk.Status != "model_pending" && wk.Status != "model_ready" {
				return errors.New("请等待用户确认形象后，再上传3D模型")
			}
			// A replacement model invalidates all old previews. Otherwise buyers
			// could see the previous character while ordering the replacement.
			wk.Model = name
			wk.Preview = []string{}
			wk.Status = "model_ready" // GLB itself is sufficient for interactive 3D preview
		}
		actor,_,_:=a.adminIdentity(r)
        recordAudit(d,actor,"work.asset",id,kind)
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
		if q.Status == "model_ready" && wk.Model == "" {
			return fmt.Errorf("缺少GLB模型")
		}
		wk.Status = q.Status
        wk.Note = strings.TrimSpace(q.Note)
        actor,_,_:=a.adminIdentity(r)
        recordAudit(d,actor,"work.status",wk.ID,q.Status)
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
		if q.Status=="canceled" && o.PaymentStartedAt!=""{
            return errors.New("订单已经发起支付，请核实微信支付状态后处理")
        }
        if q.Status=="printing" && o.RefundStatus!=""{
            return errors.New("订单存在退款申请，不能进入生产")
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
		o.History=append(o.History,OrderHistory{At:now(),Actor:"admin",Action:"status."+q.Status})
        actor,_,_:=a.adminIdentity(r)
        recordAudit(d,actor,"order.status",o.ID,q.Status)
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
