package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"sort"
	"strconv"
	"strings"
	"time"
)

// AdminPage limits server responses even if the caller supplies huge parameters.
// The SQL implementation uses COUNT + LIMIT/OFFSET; no full-table JSON download.
type AdminPage struct {
	Page     int    `json:"page"`
	PageSize int    `json:"page_size"`
	Q        string `json:"-"`
	Status   string `json:"-"`
	Category string `json:"-"`
}
type AdminPageResult[T any] struct {
	Items    []T `json:"items"`
	Total    int `json:"total"`
	Page     int `json:"page"`
	PageSize int `json:"page_size"`
}
type AdminUserRow struct {
	ID        string `json:"id"`
	CreatedAt string `json:"created_at"`
	LastLogin string `json:"last_login"`
	Disabled  bool   `json:"disabled"`
	Works     int    `json:"works"`
	Orders    int    `json:"orders"`
}

func parseAdminPage(r *http.Request) (AdminPage, error) {
	v := r.URL.Query()
	p := AdminPage{Page: 1, PageSize: 20, Q: strings.TrimSpace(v.Get("q")), Status: strings.TrimSpace(v.Get("status")), Category: strings.TrimSpace(v.Get("category"))}
	if len([]rune(p.Q)) > 100 {
		return p, fmt.Errorf("搜索内容最多100个字")
	}
	if x := v.Get("page"); x != "" {
		n, err := strconv.Atoi(x)
		if err != nil || n < 1 || n > 100000 {
			return p, fmt.Errorf("page 必须在1到100000之间")
		}
		p.Page = n
	}
	if x := v.Get("page_size"); x != "" {
		n, err := strconv.Atoi(x)
		if err != nil || n < 1 || n > 100 {
			return p, fmt.Errorf("page_size 必须在1到100之间")
		}
		p.PageSize = n
	}
	return p, nil
}
func matchText(q string, fields ...string) bool {
	if q == "" {
		return true
	}
	needle := strings.ToLower(q)
	for _, field := range fields {
		if strings.Contains(strings.ToLower(field), needle) {
			return true
		}
	}
	return false
}
func matchStatus(needle, actual string) bool { return needle == "" || actual == needle }
func matchCategory(category, status string) bool {
	if category != "shipping" {
		return true
	}
	return status == "printing" || status == "shipped" || status == "completed"
}
func pageWindow(total int, p AdminPage) (int, int) {
	start := (p.Page - 1) * p.PageSize
	if start >= total {
		return total, total
	}
	end := start + p.PageSize
	if end > total {
		end = total
	}
	return start, end
}
func (s *Store) adminWorksPage(p AdminPage) ([]*Work, int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	filtered := []*Work{}
	for _, v := range s.d.Works {
		u := s.d.Users[v.Owner]
		uid := ""
		if u != nil {
			uid = u.ID
		}
		if matchStatus(p.Status, v.Status) && matchText(p.Q, v.ID, uid, v.Status) {
			cp := *v
			cp.UserID = uid
			filtered = append(filtered, &cp)
		}
	}
	sort.Slice(filtered, func(i, j int) bool {
		if filtered[i].CreatedAt == filtered[j].CreatedAt {
			return filtered[i].ID > filtered[j].ID
		}
		return filtered[i].CreatedAt > filtered[j].CreatedAt
	})
	total := len(filtered)
	lo, hi := pageWindow(total, p)
	return filtered[lo:hi], total, nil
}
func (s *Store) adminOrdersPage(p AdminPage) ([]*Order, int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	filtered := []*Order{}
	for _, v := range s.d.Orders {
		u := s.d.Users[v.Owner]
		uid := ""
		if u != nil {
			uid = u.ID
		}
		if matchStatus(p.Status, v.Status) && matchCategory(p.Category, v.Status) && matchText(p.Q, v.ID, v.WorkID, v.Name, v.Phone, v.Tracking, uid) {
			cp := *v
			cp.UserID = uid
			filtered = append(filtered, &cp)
		}
	}
	sort.Slice(filtered, func(i, j int) bool {
		if filtered[i].CreatedAt == filtered[j].CreatedAt {
			return filtered[i].ID > filtered[j].ID
		}
		return filtered[i].CreatedAt > filtered[j].CreatedAt
	})
	total := len(filtered)
	lo, hi := pageWindow(total, p)
	return filtered[lo:hi], total, nil
}
func (s *Store) adminUsersPage(p AdminPage) ([]AdminUserRow, int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	filtered := []AdminUserRow{}
	works, orders := map[string]int{}, map[string]int{}
	for _, v := range s.d.Works {
		works[v.Owner]++
	}
	for _, v := range s.d.Orders {
		orders[v.Owner]++
	}
	for _, u := range s.d.Users {
		if !matchText(p.Q, u.ID) {
			continue
		}
		filtered = append(filtered, AdminUserRow{ID: u.ID, CreatedAt: u.CreatedAt, LastLogin: u.LastLogin, Disabled: u.Disabled, Works: works[u.Owner], Orders: orders[u.Owner]})
	}
	sort.Slice(filtered, func(i, j int) bool {
		if filtered[i].CreatedAt == filtered[j].CreatedAt {
			return filtered[i].ID > filtered[j].ID
		}
		return filtered[i].CreatedAt > filtered[j].CreatedAt
	})
	total := len(filtered)
	lo, hi := pageWindow(total, p)
	return filtered[lo:hi], total, nil
}
func sqlStatusExpr(dialect, alias string) string {
	if dialect == "postgres" {
		return alias + ".payload::jsonb->>'status'"
	}
	return "JSON_UNQUOTE(JSON_EXTRACT(" + alias + ".payload, '$.status'))"
}
func sqlFieldExpr(dialect, alias, field string) string {
	if dialect == "postgres" {
		return alias + ".payload::jsonb->>'" + field + "'"
	}
	return "JSON_UNQUOTE(JSON_EXTRACT(" + alias + ".payload, '$." + field + "'))"
}
func (s *SQLStore) pagedQuery(table string, p AdminPage, kind string) ([]string, int, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 12*time.Second)
	defer cancel()
	base := " FROM " + table + " t"
	where := []string{"1=1"}
	args := []any{}
	status := sqlStatusExpr(s.dialect, "t")
	if kind == "works" || kind == "orders" {
		base += " LEFT JOIN ty_users u ON u.id=t.owner_id"
	}
	if p.Status != "" {
		where = append(where, status+"=?")
		args = append(args, p.Status)
	}
	if p.Category == "shipping" && kind == "orders" {
		where = append(where, status+" IN ('printing','shipped','completed')")
	}
	if p.Q != "" {
		term := "%" + strings.ToLower(p.Q) + "%"
		parts := []string{"LOWER(t.id) LIKE ?"}
		if kind == "works" || kind == "orders" {
			parts = append(parts, "LOWER("+sqlFieldExpr(s.dialect, "u", "id")+") LIKE ?")
		}
		if kind == "orders" {
			for _, field := range []string{"work_id", "name", "phone", "tracking"} {
				parts = append(parts, "LOWER("+sqlFieldExpr(s.dialect, "t", field)+") LIKE ?")
			}
		} else if kind == "users" {
			parts = append(parts, "LOWER("+sqlFieldExpr(s.dialect, "t", "id")+") LIKE ?")
		}
		where = append(where, "("+strings.Join(parts, " OR ")+")")
		for range parts {
			args = append(args, term)
		}
	}
	clause := base + " WHERE " + strings.Join(where, " AND ")
	var total int
	if err := s.db.QueryRowContext(ctx, s.bind("SELECT COUNT(*)"+clause), args...).Scan(&total); err != nil {
		return nil, 0, err
	}
	if total == 0 {
		return []string{}, 0, nil
	}
	// Only request the page's payload; exclude huge unsought JSON documents.
	query := "SELECT t.payload" + clause + " ORDER BY t.created_at DESC,t.id DESC LIMIT ? OFFSET ?"
	rows, err := s.db.QueryContext(ctx, s.bind(query), append(append([]any(nil), args...), p.PageSize, (p.Page-1)*p.PageSize)...)
	if err != nil {
		return nil, 0, err
	}
	defer rows.Close()
	result := []string{}
	for rows.Next() {
		var raw string
		if err := rows.Scan(&raw); err != nil {
			return nil, 0, err
		}
		result = append(result, raw)
	}
	if err := rows.Err(); err != nil {
		return nil, 0, err
	}
	return result, total, nil
}
func (s *SQLStore) adminWorksPage(p AdminPage) ([]*Work, int, error) {
	raws, total, err := s.pagedQuery("ty_works", p, "works")
	if err != nil {
		return nil, 0, err
	}
	result := make([]*Work, 0, len(raws))
	for _, raw := range raws {
		var v Work
		if err := json.Unmarshal([]byte(raw), &v); err != nil {
			return nil, 0, err
		}
		if user, ok := s.user(v.Owner); ok {
			v.UserID = user.ID
		}
		result = append(result, &v)
	}
	return result, total, nil
}
func (s *SQLStore) adminOrdersPage(p AdminPage) ([]*Order, int, error) {
	raws, total, err := s.pagedQuery("ty_orders", p, "orders")
	if err != nil {
		return nil, 0, err
	}
	result := make([]*Order, 0, len(raws))
	for _, raw := range raws {
		var v Order
		if err := json.Unmarshal([]byte(raw), &v); err != nil {
			return nil, 0, err
		}
		if user, ok := s.user(v.Owner); ok {
			v.UserID = user.ID
		}
		result = append(result, &v)
	}
	return result, total, nil
}
func (s *SQLStore) adminUsersPage(p AdminPage) ([]AdminUserRow, int, error) {
	raws, total, err := s.pagedQuery("ty_users", p, "users")
	if err != nil {
		return nil, 0, err
	}
	result := make([]AdminUserRow, 0, len(raws))
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	for _, raw := range raws {
		var u User
		if err := json.Unmarshal([]byte(raw), &u); err != nil {
			return nil, 0, err
		}
		row := AdminUserRow{ID: u.ID, CreatedAt: u.CreatedAt, LastLogin: u.LastLogin, Disabled: u.Disabled}
		if err := s.db.QueryRowContext(ctx, s.bind("SELECT COUNT(*) FROM ty_works WHERE owner_id=?"), u.Owner).Scan(&row.Works); err != nil {
			return nil, 0, err
		}
		if err := s.db.QueryRowContext(ctx, s.bind("SELECT COUNT(*) FROM ty_orders WHERE owner_id=?"), u.Owner).Scan(&row.Orders); err != nil {
			return nil, 0, err
		}
		result = append(result, row)
	}
	return result, total, nil
}
func (a *App) adminPagedWorks(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	p, err := parseAdminPage(r)
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	switch p.Status {
	case "", "image_pending", "image_ready", "model_pending", "model_ready", "failed":
	default:
		fail(w, 400, "无效作品状态")
		return
	}
	items, total, err := a.db.adminWorksPage(p)
	if err != nil {
		fail(w, 500, "查询作品失败")
		return
	}
	for _, wk := range items {
		exposed := a.exposeWork(wk)
		*wk = *exposed
	}
	reply(w, 200, AdminPageResult[*Work]{Items: items, Total: total, Page: p.Page, PageSize: p.PageSize})
}
func (a *App) adminPagedOrders(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	p, err := parseAdminPage(r)
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	switch p.Status {
	case "", "awaiting_payment", "paid", "printing", "shipped", "completed", "canceled":
	default:
		fail(w, 400, "无效订单状态")
		return
	}
	if p.Category != "" && p.Category != "shipping" {
		fail(w, 400, "无效订单分类")
		return
	}
	items, total, err := a.db.adminOrdersPage(p)
	if err != nil {
		fail(w, 500, "查询订单失败")
		return
	}
	for _, o := range items {
		o.Owner = ""
	} // never expose OpenID
	reply(w, 200, AdminPageResult[*Order]{Items: items, Total: total, Page: p.Page, PageSize: p.PageSize})
}
func (a *App) adminPagedUsers(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	p, err := parseAdminPage(r)
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	items, total, err := a.db.adminUsersPage(p)
	if err != nil {
		fail(w, 500, "查询用户失败")
		return
	}
	reply(w, 200, AdminPageResult[AdminUserRow]{Items: items, Total: total, Page: p.Page, PageSize: p.PageSize})
}
