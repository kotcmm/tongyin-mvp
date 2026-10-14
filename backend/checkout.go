package main

import (
	"errors"
	"fmt"
	"net/http"
	"strings"
)

// 订单模块：收货地址、作品关联与下单。历史订单的收货信息是独立快照。
func validateShipping(name, phone, address string) (string, string, string, error) {
	name = strings.TrimSpace(name)
	phone = strings.TrimSpace(phone)
	address = strings.TrimSpace(address)
	if len([]rune(name)) < 2 || len([]rune(name)) > 30 || len(phone) < 6 || len(phone) > 20 || len([]rune(address)) < 8 || len([]rune(address)) > 300 {
		return "", "", "", fmt.Errorf("请填写完整收货信息")
	}
	return name, phone, address, nil
}

func (a *App) lastShippingAddress(owner string) *ShippingAddress {
	if v, ok := a.db.address(owner); ok && v.Owner == owner {
		return v
	}
	var latest *Order
	for _, o := range a.db.orders(owner, false) {
		if o.Owner != owner || o.Name == "" || o.Phone == "" || o.Address == "" {
			continue
		}
		if latest == nil || o.CreatedAt > latest.CreatedAt {
			latest = o
		}
	}
	if latest == nil {
		return nil
	}
	return &ShippingAddress{Owner: owner, Name: latest.Name, Phone: latest.Phone, Address: latest.Address, CreatedAt: latest.CreatedAt}
}

func (a *App) getShippingAddress(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	addr := a.lastShippingAddress(owner)
	if addr == nil {
		reply(w, 200, map[string]any{"address": nil})
		return
	}
	cp := *addr
	cp.Owner = ""
	reply(w, 200, map[string]any{"address": &cp})
}

func (a *App) putShippingAddress(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	var q struct {
		Name    string `json:"name"`
		Phone   string `json:"phone"`
		Address string `json:"address"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数有误")
		return
	}
	name, phone, address, err := validateShipping(q.Name, q.Phone, q.Address)
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	v := &ShippingAddress{Owner: owner, CreatedAt: now(), Name: name, Phone: phone, Address: address}
	if err := a.db.update(func(d *Data) error {
		if d.Addresses == nil {
			d.Addresses = map[string]*ShippingAddress{}
		}
		d.Addresses[owner] = v
		return nil
	}); err != nil {
		fail(w, 500, "地址保存失败")
		return
	}
	cp := *v
	cp.Owner = ""
	reply(w, 200, map[string]any{"address": &cp})
}

// Return the active order of this work without creating a duplicate.

func (a *App) getWorkOrder(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	id := r.PathValue("id")
	work, ok := a.db.work(id)
	if !ok || work.Owner != owner {
		fail(w, 404, "作品不存在")
		return
	}
	var latest *Order
	for _, o := range a.db.orders(owner, false) {
		if o.Owner != owner || o.WorkID != id || o.Status == "canceled" {
			continue
		}
		if latest == nil || o.CreatedAt > latest.CreatedAt {
			latest = o
		}
	}
	if latest == nil {
		reply(w, 200, map[string]any{"order": nil})
		return
	}
	cp := *latest
	cp.Owner = ""
	reply(w, 200, map[string]any{"order": &cp})
}

// Only an unpaid order's shipping snapshot may be updated online.
// Editing the order never rewrites a separately saved default address.

func (a *App) updateOrderShipping(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	id := r.PathValue("id")
	var q struct {
		Name    string `json:"name"`
		Phone   string `json:"phone"`
		Address string `json:"address"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数有误")
		return
	}
	name, phone, address, err := validateShipping(q.Name, q.Phone, q.Address)
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	var updated Order
	err = a.db.update(func(d *Data) error {
		order, exists := d.Orders[id]
		if !exists || order.Owner != owner {
			return errNotFound
		}
		if order.Status != "awaiting_payment" {
			return fmt.Errorf("只有待付款订单可以修改收货地址")
		}
		order.Name = name
		order.Phone = phone
		order.Address = address
		updated = *order
		return nil
	})
	if errors.Is(err, errNotFound) {
		fail(w, 404, "订单不存在")
		return
	}
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	updated.Owner = ""
	reply(w, 200, &updated)
}

func (a *App) createOrder(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	var q struct {
		WorkID  string `json:"work_id"`
		Name    string `json:"name"`
		Phone   string `json:"phone"`
		Address string `json:"address"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数有误")
		return
	}
	var validationErr error
	q.Name, q.Phone, q.Address, validationErr = validateShipping(q.Name, q.Phone, q.Address)
	if validationErr != nil {
		fail(w, 400, validationErr.Error())
		return
	}
	order := &Order{ID: newID(), Owner: owner, WorkID: q.WorkID, CreatedAt: now(), Name: q.Name, Phone: q.Phone, Address: q.Address, PriceFen: a.cfg.PriceFen, Status: "awaiting_payment", History: []OrderHistory{{At: now(), Actor: "customer", Action: "created"}}}
	err := a.db.update(func(d *Data) error {
		wk, ok := d.Works[q.WorkID]
		if !ok || wk.Owner != owner || wk.Status != "model_ready" {
			return fmt.Errorf("模型尚未准备好")
		}
		for _, o := range d.Orders {
			if o.WorkID == q.WorkID && o.Owner == owner && o.Status != "canceled" {
				return fmt.Errorf("该作品已有订单")
			}
		}
		d.Orders[order.ID] = order
		if d.Addresses == nil {
			d.Addresses = map[string]*ShippingAddress{}
		}
		d.Addresses[owner] = &ShippingAddress{Owner: owner, Name: q.Name, Phone: q.Phone, Address: q.Address, CreatedAt: now()}
		return nil
	})
	if err != nil {
		fail(w, 409, err.Error())
		return
	}
	copyOrder := *order
	copyOrder.Owner = ""
	reply(w, 201, &copyOrder)
}

func (a *App) listOrders(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	out := []*Order{}
	for _, o := range a.db.orders(owner, false) {
		o.Owner = ""
		out = append(out, o)
	}
	reply(w, 200, out)
}

func (a *App) getOrder(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	o, ok := a.db.order(r.PathValue("id"))
	if !ok || o.Owner != owner {
		fail(w, 404, "订单不存在")
		return
	}
	o.Owner = ""
	reply(w, 200, o)
}
