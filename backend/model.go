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
	ID            string   `json:"id"`
	Owner         string   `json:"owner,omitempty"`
	UserID        string   `json:"owner_user_id,omitempty"`
	CreatedAt     string   `json:"created_at"`
	Original      string   `json:"original"`
	Candidates    []string `json:"candidates"`
	Selected      int      `json:"selected"` // -1 until selected
	Preview       []string `json:"preview"`
	Model         string   `json:"model"`
	Status        string   `json:"status"` // image_pending, image_ready, model_pending, model_ready, failed
	ImageAttempts int      `json:"image_attempts"`
	GenerationID  string   `json:"generation_id,omitempty"`
	Note          string   `json:"note,omitempty"`
	AIProviderID string `json:"ai_provider_id,omitempty"`
	AIPromptVersion int `json:"ai_prompt_version,omitempty"`
	AITaskStage string `json:"ai_task_stage,omitempty"`
	AIStartedAt string `json:"ai_started_at,omitempty"`
}

type Order struct {
	ID                string          `json:"id"`
	Owner             string          `json:"owner,omitempty"`
	UserID            string          `json:"owner_user_id,omitempty"`
	WorkID            string          `json:"work_id"`
	CreatedAt         string          `json:"created_at"`
	Name              string          `json:"name"`
	Phone             string          `json:"phone"`
	Address           string          `json:"address"`
	PriceFen          int             `json:"price_fen"`
	Status            string          `json:"status"` // awaiting_payment, paid, printing, shipped, completed, canceled
	Tracking          string          `json:"tracking,omitempty"`
	PaymentStartedAt  string          `json:"payment_started_at,omitempty"`
	CancelReason      string          `json:"cancel_reason,omitempty"`
	Carrier           string          `json:"carrier,omitempty"`
	CarrierCode       string          `json:"carrier_code,omitempty"`
	ShippedAt         string          `json:"shipped_at,omitempty"`
	DeliveredAt       string          `json:"delivered_at,omitempty"`
	TrackingCheckedAt string          `json:"tracking_checked_at,omitempty"`
	TrackingState     string          `json:"tracking_state,omitempty"`
	TrackingEvents    []TrackingEvent `json:"tracking_events,omitempty"`
	History           []OrderHistory  `json:"history,omitempty"`
	ProductionNote    string          `json:"production_note,omitempty"`
	RefundStatus      string          `json:"refund_status,omitempty"`
}

type TrackingEvent struct {
	Time    string `json:"time"`
	Context string `json:"context"`
	Source  string `json:"source,omitempty"`
}
type OrderHistory struct {
	At     string `json:"at"`
	Actor  string `json:"actor"`
	Action string `json:"action"`
	Note   string `json:"note,omitempty"`
}

// The existing Owner field remains the original OpenID for backwards compatibility.
// Internal ID is a separate stable identifier used by the administrative UI.
type User struct {
	Owner     string `json:"owner"`
	ID        string `json:"id"`
	CreatedAt string `json:"created_at"`
	LastLogin string `json:"last_login"`
	Disabled  bool   `json:"disabled"`
}
type AdminUser struct {
	Owner        string `json:"owner"`
	CreatedAt    string `json:"created_at"`
	Role         string `json:"role"`
	Salt         string `json:"salt"`
	PasswordHash string `json:"password_hash"`
	Version      int    `json:"version"`
	Disabled     bool   `json:"disabled"`
}
type AuditRecord struct {
	ID        string `json:"id"`
	Owner     string `json:"owner"`
	CreatedAt string `json:"created_at"`
	Action    string `json:"action"`
	Target    string `json:"target"`
	Detail    string `json:"detail,omitempty"`
}

// ShippingAddress is private to the authenticated owner; it is never shared across users.
type ShippingAddress struct {
	Owner     string `json:"owner"`
	CreatedAt string `json:"created_at"`
	Name      string `json:"name"`
	Phone     string `json:"phone"`
	Address   string `json:"address"`
}

type Data struct {
	Works     map[string]*Work            `json:"works"`
	Orders    map[string]*Order           `json:"orders"`
	Addresses map[string]*ShippingAddress `json:"addresses"`
	Users     map[string]*User            `json:"users"`
	Admins    map[string]*AdminUser       `json:"admins"`
	Audit     map[string]*AuditRecord     `json:"audit"`
	AIProviders map[string]*AIProvider `json:"ai_providers"`
	AIPrompts map[string]*AIPrompt `json:"ai_prompts"`
}

// Repository isolates the persistence backend. Business handlers need not know SQL dialects.
type Repository interface {
	update(func(*Data) error) error
	work(string) (*Work, bool)
	order(string) (*Order, bool)
	works(string, bool) []*Work
	orders(string, bool) []*Order
	adminWorksPage(AdminPage) ([]*Work, int, error)
	adminOrdersPage(AdminPage) ([]*Order, int, error)
	adminUsersPage(AdminPage) ([]AdminUserRow, int, error)
	address(string) (*ShippingAddress, bool)
	user(string) (*User, bool)
	adminUser(string) (*AdminUser, bool)
	Close() error
}

type Store struct {
	mu   sync.Mutex
	path string
	d    Data
}

func newStore(path string) (*Store, error) {
	s := &Store{path: path, d: Data{Works: map[string]*Work{}, Orders: map[string]*Order{}, Addresses: map[string]*ShippingAddress{}, Users: map[string]*User{}, Admins: map[string]*AdminUser{}, Audit: map[string]*AuditRecord{}}}
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
	if s.d.Addresses == nil {
		s.d.Addresses = map[string]*ShippingAddress{}
	}
	if s.d.Users == nil {
		s.d.Users = map[string]*User{}
	}
	if s.d.Admins == nil {
		s.d.Admins = map[string]*AdminUser{}
	}
	if s.d.Audit == nil {
		s.d.Audit = map[string]*AuditRecord{}
	}
	if s.d.AIProviders == nil { s.d.AIProviders = map[string]*AIProvider{} }
	if s.d.AIPrompts == nil { s.d.AIPrompts = map[string]*AIPrompt{} }
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

func (s *Store) address(owner string) (*ShippingAddress, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	v, ok := s.d.Addresses[owner]
	if !ok {
		return nil, false
	}
	cp := *v
	return &cp, true
}

func (s *Store) user(owner string) (*User, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	p, ok := s.d.Users[owner]
	if !ok {
		return nil, false
	}
	cp := *p
	return &cp, true
}
func (s *Store) adminUser(name string) (*AdminUser, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	p, ok := s.d.Admins[name]
	if !ok {
		return nil, false
	}
	cp := *p
	return &cp, true
}
