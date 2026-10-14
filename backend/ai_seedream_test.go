package main

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"image"
	"image/color"
	"image/png"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func seedreamTestImage(t *testing.T, dir string) (string, []byte) {
	t.Helper()
	if err := os.MkdirAll(filepath.Join(dir, "media"), 0700); err != nil {
		t.Fatal(err)
	}
	im := image.NewRGBA(image.Rect(0, 0, 64, 64))
	im.Set(0, 0, color.RGBA{R: 220, G: 33, B: 22, A: 255})
	var buf bytes.Buffer
	if err := png.Encode(&buf, im); err != nil {
		t.Fatal(err)
	}
	name := "source.png"
	if err := os.WriteFile(filepath.Join(dir, "media", name), buf.Bytes(), 0600); err != nil {
		t.Fatal(err)
	}
	return name, buf.Bytes()
}

func TestSeedreamUsesBase64InsteadOfLocalhost(t *testing.T) {
	a, _ := aiTestApp(t)
	name, original := seedreamTestImage(t, a.cfg.DataDir)
	key := "test-api-key-do-not-leak"
	encrypted, err := a.aiEncrypt(key)
	if err != nil {
		t.Fatal(err)
	}
	gotRequest := false
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotRequest = true
		if r.Method != http.MethodPost || r.Header.Get("Authorization") != "Bearer "+key {
			t.Errorf("wrong request method or authorization")
		}
		var payload map[string]any
		if e := json.NewDecoder(r.Body).Decode(&payload); e != nil {
			t.Errorf("invalid JSON: %v", e)
			return
		}
		encoded, _ := payload["image"].(string)
		const prefix = "data:image/png;base64,"
		if !strings.HasPrefix(encoded, prefix) {
			t.Errorf("reference image was not sent inline")
		} else {
			raw, e := base64.StdEncoding.DecodeString(strings.TrimPrefix(encoded, prefix))
			if e != nil || !bytes.Equal(raw, original) {
				t.Errorf("reference image changed on upload")
			}
		}
		if strings.Contains(encoded, "localhost") || payload["sequential_image_generation"] != nil {
			t.Errorf("local URL or unsupported group parameter sent")
		}
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("X-Tt-Logid", "testrequest123")
		w.WriteHeader(http.StatusBadRequest)
		_, _ = w.Write([]byte(`{"error":{"code":"InvalidParameter.Image","message":"Image URL http://localhost:18080/media/a?signature=secret cannot be accessed; key test-api-key-do-not-leak"}}`))
	}))
	defer server.Close()
	p := &AIProvider{Adapter: "seedream", Stage: "image", Endpoint: server.URL, Model: "test-seedream", APIKeyCipher: encrypted}
	wk := &Work{ID: "test-work", GenerationID: "test-generation"}
	err = a.generateSeedream(wk, "image", name, p, AIPrompt{Template: "保留参考画作"})
	if !gotRequest || err == nil {
		t.Fatalf("mock provider not called or error missing: %v", err)
	}
	msg := err.Error()
	if !strings.Contains(msg, "InvalidParameter.Image") || strings.Contains(msg, "test-api-key-do-not-leak") || strings.Contains(msg, "localhost") || strings.Contains(msg, "signature=secret") {
		t.Fatalf("provider error was not sanitized: %s", msg)
	}
}

func TestSeedreamRejectsUnreadableOrInvalidOriginal(t *testing.T) {
	dir := t.TempDir()
	if _, err := seedreamImageDataURL(dir, "../sensitive"); err == nil {
		t.Fatal("path traversal was accepted")
	}
	if err := os.MkdirAll(filepath.Join(dir, "media"), 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "media", "bad.png"), []byte("plain text"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := seedreamImageDataURL(dir, "bad.png"); err == nil {
		t.Fatal("non-image data was accepted")
	}
}
