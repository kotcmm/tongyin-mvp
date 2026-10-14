package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

var seedreamExternalURL = regexp.MustCompile(`(?i)(?:https?://|data:image/)[^\s"'<>]+`)
var seedreamSafeErrorCode = regexp.MustCompile(`^[A-Za-z0-9_.-]{1,100}$`)

// seedreamImageDataURL eliminates the public-image-URL dependency and avoids
// sending signed customer media URLs to third-party APIs.
func seedreamImageDataURL(dataDir, source string) (string, error) {
	if source == "" || filepath.Base(source) != source || strings.ContainsAny(source, "/\\") {
		return "", fmt.Errorf("原画文件名无效")
	}
	f, err := os.Open(filepath.Join(dataDir, "media", source))
	if err != nil {
		return "", fmt.Errorf("无法读取本地原画")
	}
	defer f.Close()
	raw, err := io.ReadAll(io.LimitReader(f, maxImageUpload+1))
	if err != nil {
		return "", fmt.Errorf("读取原画失败")
	}
	if len(raw) == 0 || int64(len(raw)) > maxImageUpload {
		return "", fmt.Errorf("原画为空或超出10MiB限制")
	}
	mime := http.DetectContentType(raw)
	switch mime {
	case "image/png", "image/jpeg", "image/webp":
		return "data:" + mime + ";base64," + base64.StdEncoding.EncodeToString(raw), nil
	default:
		return "", fmt.Errorf("原画必须是 PNG、JPEG 或 WebP")
	}
}

// Provider error bodies are untrusted. Keep the useful code/message but never
// disclose API credentials, signed URLs, or base64 drawing data in logs/UI.
func seedreamResponseError(resp *http.Response, apiKey string) error {
	var envelope struct {
		Error struct {
			Code    string `json:"code"`
			Message string `json:"message"`
		} `json:"error"`
		RequestID string `json:"request_id"`
	}
	_ = json.NewDecoder(io.LimitReader(resp.Body, 32<<10)).Decode(&envelope)
	code := envelope.Error.Code
	if !seedreamSafeErrorCode.MatchString(code) {
		code = "UnknownError"
	}
	detail := strings.TrimSpace(envelope.Error.Message)
	if apiKey != "" {
		detail = strings.ReplaceAll(detail, apiKey, "[密钥已隐藏]")
	}
	detail = seedreamExternalURL.ReplaceAllString(detail, "[地址已隐藏]")
	// Strip control characters before persisting the provider message in a work.
	detail = strings.Map(func(r rune) rune {
		if r < 32 || r == 127 {
			return ' '
		}
		return r
	}, detail)
	if len([]rune(detail)) > 200 {
		detail = string([]rune(detail)[:200]) + "..."
	}
	if detail == "" {
		detail = "模型请求失败，请核对模型 ID、账户权限及输入图片"
	}
	requestID := resp.Header.Get("X-Tt-Logid")
	if requestID == "" {
		requestID = envelope.RequestID
	}
	if len(requestID) > 100 {
		requestID = requestID[:100]
	}
	if requestID != "" && !seedreamSafeErrorCode.MatchString(requestID) {
		requestID = ""
	}
	log.Printf("Seedream rejected request: HTTP %d, code=%s, request_id=%s, detail=%s", resp.StatusCode, code, requestID, detail)
	return fmt.Errorf("Seedream HTTP %d (%s): %s", resp.StatusCode, code, detail)
}
