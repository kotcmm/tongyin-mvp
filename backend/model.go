package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sync"
	"time"
)

type Work struct {
	ID         string   `json:"id"`
	Owner      string   `json:"owner,omitempty"`
	CreatedAt  string   `json:"created_at"`
	Original   string   `json:"original"`
	Candidates []string `json:"candidates"`
	Selected   int      `json:"selected"` // -1 until selected
	Preview    []string `json:"preview"`
	Model      string   `json:"model"`
	Status     string   `json:"status"` // image_pending, image_ready, model_pending, model_ready, failed
	Note       string   `json:"note,omitempty"`
}

type Order struct {
	ID        string `json:"id"`
	Owner     string `json:"owner,omitempty"`
	WorkID    string `json:"work_id"`
	CreatedAt string `json:"created_at"`
	Name      string `json:"name"`
	Phone     string `json:"phone"`
	Address   string `json:"address"`
	PriceFen  int    `json:"price_fen"`
	Status    string `json:"status"` // awaiting_payment, paid, printing, shipped, completed, canceled
	Tracking  string `json:"tracking,omitempty"`
}

type Data struct {
	Works  map[string]*Work  `json:"works"`
	Orders map[string]*Order `json:"orders"`
}

// Repository isolates the persistence backend. Business handlers need not know SQL dialects.
type Repository interface {
	update(func(*Data) error) error
	work(string) (*Work, bool)
	order(string) (*Order, bool)
	works(string, bool) []*Work
	orders(string, bool) []*Order
	Close() error
}

type Store struct {
	mu   sync.Mutex
	path string
	d    Data
}

func newStore(path string) (*Store, error) {
	s := &Store{path: path, d: Data{Works: map[string]*Work{}, Orders: map[string]*Order{}}}
	b, e := os.ReadFile(path)
	if e == nil {
		if err := json.Unmarshal(b, &s.d); err != nil {
			return nil, err
		}
	} else if !os.IsNotExist(e) {
		return nil, e
	}
	if s.d.Works == nil {
		s.d.Works = map[string]*Work{}
	}
	if s.d.Orders == nil {
		s.d.Orders = map[string]*Order{}
	}
	return s, nil
}
func (s *Store) Close() error { return nil }

func (s *Store) update(fn func(*Data) error) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	// Copy-on-write keeps in-memory state coherent if disk write fails.
	b, _ := json.Marshal(s.d)
	var next Data
	_ = json.Unmarshal(b, &next)
	if err := fn(&next); err != nil {
		return err
	}
	payload, err := json.MarshalIndent(next, "", "  ")
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(s.path), 0700); err != nil {
		return err
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, payload, 0600); err != nil {
		return err
	}
	if err := os.Rename(tmp, s.path); err != nil {
		return err
	}
	s.d = next
	return nil
}
func (s *Store) work(id string) (*Work, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	w, ok := s.d.Works[id]
	if !ok {
		return nil, false
	}
	b, _ := json.Marshal(w)
	var v Work
	_ = json.Unmarshal(b, &v)
	v.Owner = w.Owner
	return &v, true
}
func (s *Store) order(id string) (*Order, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	o, ok := s.d.Orders[id]
	if !ok {
		return nil, false
	}
	b, _ := json.Marshal(o)
	var v Order
	_ = json.Unmarshal(b, &v)
	v.Owner = o.Owner
	return &v, true
}
func (s *Store) works(owner string, all bool) []*Work {
	s.mu.Lock()
	defer s.mu.Unlock()
	v := []*Work{}
	for _, w := range s.d.Works {
		if all || w.Owner == owner {
			b, _ := json.Marshal(w)
			var c Work
			_ = json.Unmarshal(b, &c)
			c.Owner = w.Owner
			v = append(v, &c)
		}
	}
	return v
}
func (s *Store) orders(owner string, all bool) []*Order {
	s.mu.Lock()
	defer s.mu.Unlock()
	v := []*Order{}
	for _, o := range s.d.Orders {
		if all || o.Owner == owner {
			b, _ := json.Marshal(o)
			var c Order
			_ = json.Unmarshal(b, &c)
			c.Owner = o.Owner
			v = append(v, &c)
		}
	}
	return v
}

var errNotFound = errors.New("not found")

func newID() string { var b [12]byte; _, _ = rand.Read(b[:]); return hex.EncodeToString(b[:]) }
func now() string   { return time.Now().Format(time.RFC3339) }
