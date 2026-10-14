package main

import (
	"context"
	"crypto/tls"
	"errors"
	"io"
	"net"
	"net/http"
	"net/http/httptrace"
	"strings"
	"sync"
)

// seedreamNetworkTrace records only transport milestones, not HTTP headers,
// query strings, API keys or the children's original drawing.
type seedreamNetworkTrace struct {
	mu     sync.Mutex
	phase  string
	reused bool
	proxy  bool
}

func (t *seedreamNetworkTrace) setPhase(phase string) {
	t.mu.Lock()
	t.phase = phase
	t.mu.Unlock()
}

func (t *seedreamNetworkTrace) snapshot() (string, bool) {
	t.mu.Lock()
	defer t.mu.Unlock()
	return t.phase, t.reused
}

func seedreamTracedRequest(req *http.Request) (*http.Request, *seedreamNetworkTrace) {
	t := &seedreamNetworkTrace{phase: "request_prepared"}
	proxyURL, err := http.ProxyFromEnvironment(req)
	t.proxy = err == nil && proxyURL != nil
	hooks := &httptrace.ClientTrace{
		DNSStart: func(httptrace.DNSStartInfo) {
			t.setPhase("dns_lookup")
		},
		DNSDone: func(info httptrace.DNSDoneInfo) {
			if info.Err != nil {
				t.setPhase("dns_failed")
			} else {
				t.setPhase("dns_resolved")
			}
		},
		ConnectStart: func(_, _ string) {
			t.setPhase("tcp_connecting")
		},
		ConnectDone: func(_, _ string, err error) {
			if err != nil {
				t.setPhase("tcp_failed")
			} else {
				t.setPhase("tcp_connected")
			}
		},
		TLSHandshakeStart: func() {
			t.setPhase("tls_handshaking")
		},
		TLSHandshakeDone: func(_ tls.ConnectionState, err error) {
			if err != nil {
				t.setPhase("tls_failed")
			} else {
				t.setPhase("tls_connected")
			}
		},
		GotConn: func(info httptrace.GotConnInfo) {
			t.mu.Lock()
			t.phase = "connection_ready"
			t.reused = info.Reused
			t.mu.Unlock()
		},
		WroteHeaders: func() {
			t.setPhase("headers_written")
		},
		WroteRequest: func(info httptrace.WroteRequestInfo) {
			if info.Err != nil {
				t.setPhase("request_write_failed")
			} else {
				t.setPhase("request_written")
			}
		},
		GotFirstResponseByte: func() {
			t.setPhase("response_started")
		},
	}
	return req.WithContext(httptrace.WithClientTrace(req.Context(), hooks)), t
}

func seedreamTransportErrorKind(err error) string {
	if err == nil {
		return "none"
	}
	switch {
	case errors.Is(err, io.ErrUnexpectedEOF):
		return "unexpected_eof"
	case errors.Is(err, io.EOF):
		return "eof"
	case errors.Is(err, context.DeadlineExceeded):
		return "deadline_exceeded"
	}
	var networkError net.Error
	if errors.As(err, &networkError) && networkError.Timeout() {
		return "network_timeout"
	}
	// Do not copy err.Error() to diagnostic logs: proxy errors may include
	// credential-bearing URLs or HTTP request metadata.
	message := strings.ToLower(err.Error())
	switch {
	case strings.Contains(message, "certificate") || strings.Contains(message, "x509"):
		return "tls_certificate_error"
	case strings.Contains(message, "tls"):
		return "tls_error"
	case strings.Contains(message, "proxy"):
		return "proxy_error"
	case strings.Contains(message, "connection refused"):
		return "connection_refused"
	case strings.Contains(message, "connection reset"):
		return "connection_reset"
	default:
		return "transport_error"
	}
}
