package main

import (
	"fmt"
	"net/http"
)

// 作品模块：从上传原画、生成形象到确认模型；所有读写均按微信用户归属校验。
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
	if a.cfg.FreeImageAttempts > 0 {
		return a.cfg.FreeImageAttempts
	}
	return 2
}

// First generation and each retry create one image; cost is limited server-side.

func (a *App) startImageGeneration(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	id := r.PathValue("id")
	var task *Work
	err := a.db.update(func(d *Data) error {
		v, ok := d.Works[id]
		if !ok || v.Owner != owner {
			return errNotFound
		}
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
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	a.dispatchAI(task, "image")
	reply(w, 202, map[string]any{"status": "image_pending", "image_attempts": task.ImageAttempts, "free_image_attempts": a.imageLimit(), "message": a.taskMessage()})
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
