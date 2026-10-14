package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

// 媒体模块：统一管理原画、形象图片和 GLB 的访问签名及上传。
func (a *App) signedMedia(name string) string {
	if name == "" {
		return ""
	}
	exp := strconv.FormatInt(time.Now().Add(48*time.Hour).Unix(), 10)
	text := name + ":" + exp
	mac := hmac.New(sha256.New, []byte(a.cfg.SessionKey))
	mac.Write([]byte(text))
	return a.cfg.PublicURL + "/media/" + name + "?expires=" + exp + "&signature=" + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}

func (a *App) serveMedia(w http.ResponseWriter, r *http.Request) {
	name := r.PathValue("file")
	exp := r.URL.Query().Get("expires")
	sig := r.URL.Query().Get("signature")
	if name == "" || strings.ContainsAny(name, "/\\") || strings.Contains(name, "..") || len(name) > 100 {
		http.NotFound(w, r)
		return
	}
	t, e := strconv.ParseInt(exp, 10, 64)
	if e != nil || t < time.Now().Unix() {
		http.NotFound(w, r)
		return
	}
	raw, e := base64.RawURLEncoding.DecodeString(sig)
	if e != nil {
		http.NotFound(w, r)
		return
	}
	mac := hmac.New(sha256.New, []byte(a.cfg.SessionKey))
	mac.Write([]byte(name + ":" + exp))
	if !hmac.Equal(mac.Sum(nil), raw) {
		http.NotFound(w, r)
		return
	}
	w.Header().Set("Cache-Control", "private,max-age=120")
	http.ServeFile(w, r, filepath.Join(a.cfg.DataDir, "media", name))
}

func uploadFailure(w http.ResponseWriter, err error) {
	if errors.Is(err, errUploadTooLarge) {
		fail(w, http.StatusRequestEntityTooLarge, err.Error())
		return
	}
	fail(w, http.StatusBadRequest, err.Error())
}

func (a *App) upload(w http.ResponseWriter, r *http.Request, allowedImagesOnly bool) (string, error) {
	limit := maxModelUpload
	if allowedImagesOnly {
		limit = maxImageUpload
	}
	maxBody := limit + multipartOverhead
	if r.ContentLength > maxBody {
		return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
	}
	r.Body = http.MaxBytesReader(w, r.Body, maxBody)
	defer func() {
		if r.MultipartForm != nil {
			_ = r.MultipartForm.RemoveAll()
		}
	}()
	if err := r.ParseMultipartForm(8 << 20); err != nil {
		var maxErr *http.MaxBytesError
		if errors.As(err, &maxErr) {
			return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
		}
		return "", fmt.Errorf("解析上传文件失败：%w", err)
	}
	f, hdr, err := r.FormFile("file")
	if err != nil {
		return "", fmt.Errorf("未找到上传文件：%w", err)
	}
	defer f.Close()
	if hdr.Size > limit {
		return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
	}
	buf := make([]byte, 512)
	n, err := f.Read(buf)
	if err != nil && err != io.EOF {
		return "", err
	}
	if n == 0 {
		return "", fmt.Errorf("上传文件为空")
	}
	kind := http.DetectContentType(buf[:n])
	ext := ""
	switch kind {
	case "image/jpeg":
		ext = ".jpg"
	case "image/png":
		ext = ".png"
	case "image/webp":
		ext = ".webp"
	}
	if !allowedImagesOnly && n >= 4 && string(buf[:4]) == "glTF" {
		ext = ".glb"
	}
	if allowedImagesOnly && ext == "" {
		return "", fmt.Errorf("仅支持 JPG、PNG、WebP 图片")
	}
	if !allowedImagesOnly && ext != ".glb" {
		return "", fmt.Errorf("模型文件必须是 GLB 格式（不支持直接上传 STL、OBJ）")
	}
	name := newID() + ext
	path := filepath.Join(a.cfg.DataDir, "media", name)
	out, err := os.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return "", err
	}
	// Copy one byte beyond the limit so an oversized upload never succeeds as
	// a truncated, corrupted GLB. Close before removal on Windows.
	first, err := out.Write(buf[:n])
	total := int64(first)
	if err == nil {
		var copied int64
		copied, err = io.Copy(out, io.LimitReader(f, limit-total+1))
		total += copied
	}
	closeErr := out.Close()
	if err != nil || closeErr != nil || total > limit {
		_ = os.Remove(path)
		if total > limit {
			return "", fmt.Errorf("%w：图片最多10MiB，GLB模型最多100MiB", errUploadTooLarge)
		}
		if err != nil {
			return "", fmt.Errorf("写入上传文件失败：%w", err)
		}
		return "", fmt.Errorf("保存上传文件失败：%w", closeErr)
	}
	return name, nil
}

func (a *App) exposeWork(wk *Work) *Work {
	cp := *wk
	cp.Owner = ""
	cp.Original = a.signedMedia(cp.Original)
	cp.Model = a.signedMedia(cp.Model)
	cp.Candidates = make([]string, len(wk.Candidates))
	for i, n := range wk.Candidates {
		cp.Candidates[i] = a.signedMedia(n)
	}
	cp.Preview = make([]string, len(wk.Preview))
	for i, n := range wk.Preview {
		cp.Preview[i] = a.signedMedia(n)
	}
	return &cp
}

// Image files are capped at 10 MiB; printable GLB models may be much larger.
// A small multipart overhead allowance is included in the request size limit.
const (
	maxImageUpload    = int64(10 << 20)
	maxModelUpload    = int64(100 << 20)
	multipartOverhead = int64(1 << 20)
)

var errUploadTooLarge = errors.New("上传文件超过允许大小")
