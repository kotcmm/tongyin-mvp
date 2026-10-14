package main

import (
	"context"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestSeedreamTransportErrorClassification(t *testing.T) {
	tests := []struct {
		err  error
		want string
	}{
		{err: io.ErrUnexpectedEOF, want: "unexpected_eof"},
		{err: io.EOF, want: "eof"},
		{err: context.DeadlineExceeded, want: "deadline_exceeded"},
		{err: errors.New("connection refused"), want: "connection_refused"},
		{err: errors.New("x509: unknown authority"), want: "tls_certificate_error"},
		{err: errors.New("connection reset by peer"), want: "connection_reset"},
	}
	for _, tc := range tests {
		if got := seedreamTransportErrorKind(tc.err); got != tc.want {
			t.Errorf("classify %v: got %q, want %q", tc.err, got, tc.want)
		}
	}
}

func TestSeedreamTransportTraceRecordsResponse(t *testing.T) {
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ok"))
	}))
	defer s.Close()
	req, err := http.NewRequest("POST", s.URL, nil)
	if err != nil {
		t.Fatal(err)
	}
	traced, probe := seedreamTracedRequest(req)
	res, err := http.DefaultClient.Do(traced)
	if err != nil {
		t.Fatal(err)
	}
	_, _ = io.Copy(io.Discard, res.Body)
	_ = res.Body.Close()
	phase, _ := probe.snapshot()
	if phase != "response_started" {
		t.Fatalf("expected response_started, got %q", phase)
	}
}
