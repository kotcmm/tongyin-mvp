package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"time"
)

// AI service is a swappable async worker. An unset AI_WORKER_URL leaves jobs in
// the admin queue for manual processing; it never invents AI results.
func (a *App) taskMessage() string {
	if a.cfg.AIWorkerURL == "" {
		return "已进入人工处理队列，请在运营后台上传生成结果"
	}
	return "AI 生成任务已提交"
}
func (a *App) dispatchAI(wk *Work, stage string) {
	if a.cfg.AIWorkerURL == "" {
		return
	}
	source := wk.Original
	if stage == "model" {
		if wk.Selected < 0 || wk.Selected >= len(wk.Candidates) {
			return
		}
		source = wk.Candidates[wk.Selected]
	}
	data, _ := json.Marshal(map[string]any{"work_id": wk.ID, "stage": stage, "source_url": a.signedMedia(source), "callback_url": a.cfg.PublicURL + "/api/internal/ai-result", "instructions": "Preserve distinctive features of original children's drawing. Single full-body printable character, neutral background, no unsupported thin parts. For image stage return exactly two candidate images; for model stage return GLB and 1-4 white clay render previews."})
	go func() {
		req, err := http.NewRequest("POST", a.cfg.AIWorkerURL, bytes.NewReader(data))
		if err != nil {
			return
		}
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", "Bearer "+a.cfg.AIWorkerToken)
		c := http.Client{Timeout: 15 * time.Second}
		resp, err := c.Do(req)
		if err != nil {
			log.Printf("AI worker submit error: %v", err)
			return
		}
		defer resp.Body.Close()
		if resp.StatusCode >= 300 {
			log.Printf("AI worker submit status: %d", resp.StatusCode)
		}
	}()
}
func (a *App) aiResult(w http.ResponseWriter, r *http.Request) {
	if a.cfg.AIWebhookToken == "" || r.Header.Get("X-AI-Token") != a.cfg.AIWebhookToken {
		fail(w, 401, "无权限")
		return
	}
	var q struct {
		WorkID   string   `json:"work_id"`
		Stage    string   `json:"stage"`
		Error    string   `json:"error"`
		URLs     []string `json:"urls"`
		ModelURL string   `json:"model_url"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数错误")
		return
	}
	wk, ok := a.db.work(q.WorkID)
	if !ok {
		fail(w, 404, "作品不存在")
		return
	}
	if q.Error != "" {
		_ = a.db.update(func(d *Data) error { d.Works[wk.ID].Status = "failed"; d.Works[wk.ID].Note = q.Error; return nil })
		reply(w, 200, map[string]string{"status": "failed"})
		return
	}
	if q.Stage != "image" && q.Stage != "model" {
		fail(w, 400, "stage 无效")
		return
	}
	if q.Stage == "image" && len(q.URLs) != 2 {
		fail(w, 400, "图片阶段必须返回两张候选图")
		return
	}
	if q.Stage == "model" && (q.ModelURL == "" || len(q.URLs) < 1 || len(q.URLs) > 4) {
		fail(w, 400, "模型阶段必须包含模型URL和1-4张预览图")
		return
	}
	local := []string{}
	for _, u := range q.URLs {
		n, err := a.downloadResult(u, true)
		if err != nil {
			fail(w, 502, "下载生成结果失败: "+err.Error())
			return
		}
		local = append(local, n)
	}
	model := ""
	if q.Stage == "model" {
		var err error
		model, err = a.downloadResult(q.ModelURL, false)
		if err != nil {
			fail(w, 502, "模型文件下载失败: "+err.Error())
			return
		}
	}
	err := a.db.update(func(d *Data) error {
		v, ok := d.Works[q.WorkID]
		if !ok {
			return errNotFound
		}
		if q.Stage == "image" {
			if v.Status != "image_pending" {
				return fmt.Errorf("任务状态已变化")
			}
			v.Candidates = local
			v.Status = "image_ready"
		} else {
			if v.Status != "model_pending" {
				return fmt.Errorf("任务状态已变化")
			}
			v.Model = model
			v.Preview = local
			v.Status = "model_ready"
		}
		return nil
	})
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	reply(w, 200, map[string]string{"status": "ok"})
}
func (a *App) downloadResult(raw string, img bool) (string, error) {
	u, err := url.Parse(raw)
	if err != nil || u.Scheme != "https" || u.Host == "" || u.User != nil {
		return "", fmt.Errorf("只接受 HTTPS 结果地址")
	}
	client := http.Client{Timeout: 60 * time.Second, CheckRedirect: func(req *http.Request, via []*http.Request) error {
		if len(via) >= 3 || req.URL.Scheme != "https" {
			return fmt.Errorf("非法重定向")
		}
		return nil
	}}
	res, err := client.Get(raw)
	if err != nil {
		return "", err
	}
	defer res.Body.Close()
	if res.StatusCode != 200 {
		return "", fmt.Errorf("上游返回 %d", res.StatusCode)
	}
	limit := int64(25 << 20)
	if img {
		limit = 10 << 20
	}
	body, err := io.ReadAll(io.LimitReader(res.Body, limit+1))
	if err != nil {
		return "", err
	}
	if int64(len(body)) > limit {
		return "", fmt.Errorf("文件太大")
	}
	mime := http.DetectContentType(body)
	ext := ""
	switch mime {
	case "image/png":
		ext = ".png"
	case "image/jpeg":
		ext = ".jpg"
	case "image/webp":
		ext = ".webp"
	default:
		if !img && len(body) >= 4 && string(body[:4]) == "glTF" {
			ext = ".glb"
		}
	}
	if ext == "" {
		return "", fmt.Errorf("结果不是有效的图片或 GLB 文件")
	}
	name := newID() + ext
	if err := os.WriteFile(filepath.Join(a.cfg.DataDir, "media", name), body, 0600); err != nil {
		return "", err
	}
	return name, nil
}

// Silence unused "time" import not needed: timeouts above use time package.
