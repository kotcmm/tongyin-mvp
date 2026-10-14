package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"time"
)

// Choose the configured default model by stage; the original worker environment
// variables remain a compatibility fallback when no provider is selected.
func (a *App) aiAutoEnabled() bool {
	p, _, err := a.aiSnapshot("image")
	return err == nil && (p != nil || a.cfg.AIWorkerURL != "")
}
func (a *App) taskMessage(stage string) string {
	p, _, err := a.aiSnapshot(stage)
	if err != nil {
		return "读取 AI 配置失败"
	}
	if p == nil && a.cfg.AIWorkerURL == "" {
		return "已进入人工处理队列，请在运营后台上传生成结果"
	}
	return "AI 生成任务已提交"
}
func (a *App) dispatchAI(wk *Work, stage string) {
	p, prompt, err := a.aiSnapshot(stage)
	if err != nil {
		a.aiTaskFailed(wk, stage, "读取 AI 配置失败")
		return
	}
	if p == nil && a.cfg.AIWorkerURL == "" {
		return
	}
	source := wk.Original
	if stage == "model" {
		if wk.Selected < 0 || wk.Selected >= len(wk.Candidates) {
			return
		}
		source = wk.Candidates[wk.Selected]
	}
	providerID := "legacy-worker"
	if p != nil {
		providerID = p.ID
	}
	_ = a.db.update(func(d *Data) error {
		if v := d.Works[wk.ID]; v != nil && v.GenerationID == wk.GenerationID {
			v.AIProviderID = providerID
			v.AIPromptVersion = prompt.Version
			v.AITaskStage = stage
			v.AIStartedAt = now()
		}
		return nil
	})
	go func() {
		var e error
		if p != nil && p.Adapter == "seedream" {
			e = a.generateSeedream(wk, stage, source, p, prompt)
		} else {
			e = a.submitAIWorker(wk, stage, source, p, prompt)
		}
		if e != nil {
			log.Printf("AI generation error work=%s stage=%s: %v", wk.ID, stage, e)
			a.aiTaskFailed(wk, stage, "AI 生成失败："+e.Error())
		}
	}()
}
func (a *App) aiTaskFailed(wk *Work, stage, message string) {
	expected := "image_pending"
	if stage == "model" {
		expected = "model_pending"
	}
	_ = a.db.update(func(d *Data) error {
		v := d.Works[wk.ID]
		if v != nil && v.Status == expected && v.GenerationID == wk.GenerationID {
			v.Status = "failed"
			v.Note = message
		}
		return nil
	})
}
func (a *App) submitAIWorker(wk *Work, stage, source string, p *AIProvider, prompt AIPrompt) error {
	endpoint := a.cfg.AIWorkerURL
	secret := a.cfg.AIWorkerToken
	model := ""
	if p != nil {
		endpoint = p.Endpoint
		model = p.Model
		var err error
		secret, err = a.aiDecrypt(p.APIKeyCipher)
		if err != nil {
			return err
		}
	}
	data, _ := json.Marshal(map[string]any{"work_id": wk.ID, "stage": stage, "generation_id": wk.GenerationID, "source_url": a.signedMedia(source), "callback_url": a.cfg.PublicURL + "/api/internal/ai-result", "instructions": prompt.Template, "prompt_version": prompt.Version, "model": model})
	req, err := http.NewRequest("POST", endpoint, bytes.NewReader(data))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")
	if secret != "" {
		req.Header.Set("Authorization", "Bearer "+secret)
	}
	c := http.Client{Timeout: 20 * time.Second}
	resp, err := c.Do(req)
	if err != nil {
		return fmt.Errorf("Worker 连接失败: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("Worker 返回 HTTP %d", resp.StatusCode)
	}
	return nil
}
func (a *App) generateSeedream(wk *Work, stage, source string, p *AIProvider, prompt AIPrompt) error {
	if stage != "image" {
		return fmt.Errorf("Seedream 不支持3D生成")
	}
	key, err := a.aiDecrypt(p.APIKeyCipher)
	if err != nil {
		return err
	}
	imageData, err := seedreamImageDataURL(a.cfg.DataDir, source)
	if err != nil {
		return fmt.Errorf("读取儿童原画失败: %w", err)
	}
	// Pass the original image inline: a local PUBLIC_URL is not reachable from Ark.
	// Single-image generation is the default across supported Seedream versions;
	// 5.0 pro/flash do not accept sequential_image_generation.
	data, _ := json.Marshal(map[string]any{"model": p.Model, "prompt": prompt.Template, "image": imageData, "size": "2K", "response_format": "url", "watermark": false})
	req, err := http.NewRequest("POST", p.Endpoint, bytes.NewReader(data))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+key)
	// A request may be accepted and billed before the connection breaks.
	// Collect transport phases without logging credentials, payloads or signed URLs.
	traceID := newID()
	tracedReq, netTrace := seedreamTracedRequest(req)
	started := time.Now()
	log.Printf("Seedream request started trace=%s work=%s model=%q body_bytes=%d image_data_bytes=%d proxy=%t timeout=190s", traceID, wk.ID, p.Model, len(data), len(imageData), netTrace.proxy)
	c := http.Client{Timeout: 190 * time.Second}
	resp, err := c.Do(tracedReq)
	if err != nil {
		phase, reused := netTrace.snapshot()
		kind := seedreamTransportErrorKind(err)
		log.Printf("Seedream transport failed trace=%s work=%s phase=%s reused=%t proxy=%t elapsed_ms=%d error_kind=%s", traceID, wk.ID, phase, reused, netTrace.proxy, time.Since(started).Milliseconds(), kind)
		return fmt.Errorf("Seedream 连接中断（%s，阶段 %s，诊断编号 %s），请查看后端日志；暂勿重复生成以免重复计费", kind, phase, traceID)
	}
	defer resp.Body.Close()
	log.Printf("Seedream response received trace=%s work=%s status=%d protocol=%s elapsed_ms=%d", traceID, wk.ID, resp.StatusCode, resp.Proto, time.Since(started).Milliseconds())
	if resp.StatusCode != 200 {
		return seedreamResponseError(resp, key)
	}
	var v struct {
		Data []struct {
			URL string `json:"url"`
		} `json:"data"`
	}
	if err := json.NewDecoder(io.LimitReader(resp.Body, 2<<20)).Decode(&v); err != nil {
		log.Printf("Seedream response parse failed trace=%s work=%s status=%d elapsed_ms=%d error_kind=%s", traceID, wk.ID, resp.StatusCode, time.Since(started).Milliseconds(), seedreamTransportErrorKind(err))
		return fmt.Errorf("Seedream 响应读取或解析失败（诊断编号 %s），请查看后端日志", traceID)
	}
	if len(v.Data) != 1 || v.Data[0].URL == "" {
		log.Printf("Seedream invalid image output trace=%s work=%s image_count=%d elapsed_ms=%d", traceID, wk.ID, len(v.Data), time.Since(started).Milliseconds())
		return fmt.Errorf("Seedream 未返回单张有效图片")
	}
	name, err := a.downloadResult(v.Data[0].URL, true)
	if err != nil {
		return fmt.Errorf("保存图片失败: %w", err)
	}
	err = a.db.update(func(d *Data) error {
		v := d.Works[wk.ID]
		if v == nil || v.Status != "image_pending" || v.GenerationID != wk.GenerationID {
			return fmt.Errorf("生成任务已过期")
		}
		v.Candidates = []string{name}
		v.Status = "image_ready"
		v.Note = ""
		return nil
	})
	if err != nil {
		_ = os.Remove(filepath.Join(a.cfg.DataDir, "media", name))
		return err
	}
	log.Printf("Seedream image saved trace=%s work=%s elapsed_ms=%d", traceID, wk.ID, time.Since(started).Milliseconds())
	return nil
}
