package main

import (
	"errors"
	"net/http"
	"strings"
)

// 履约模块：取消与退款状态应如实记录，禁止将退款申请等同于资金到账。
func (a *App) adminRefundRequest(w http.ResponseWriter, r *http.Request) {
	if !a.admin(w, r) {
		return
	}
	// No fake "refunded" transition: operator must complete a real merchant refund.
	var q struct {
		Reason string `json:"reason"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数错误")
		return
	}
	q.Reason = strings.TrimSpace(q.Reason)
	if len([]rune(q.Reason)) < 3 || len([]rune(q.Reason)) > 200 {
		fail(w, 400, "请填写退款原因")
		return
	}
	actor, _, _ := a.adminIdentity(r)
	err := a.db.update(func(d *Data) error {
		o, ok := d.Orders[r.PathValue("id")]
		if !ok {
			return errNotFound
		}
		if o.Status != "paid" && o.Status != "printing" {
			return errors.New("只有已付款或制作中的订单能申请退款")
		}
		if o.RefundStatus != "" {
			return errors.New("该订单已经有退款记录")
		}
		o.RefundStatus = "requested"
		o.History = append(o.History, OrderHistory{At: now(), Actor: actor, Action: "refund_requested", Note: q.Reason})
		recordAudit(d, actor, "order.refund_requested", o.ID, q.Reason)
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
	reply(w, 200, map[string]any{"refund_status": "requested", "message": "已记录退款申请，请在微信商户平台执行实际退款并核对资金；系统未宣称退款成功"})
}

func (a *App) cancelOrder(w http.ResponseWriter, r *http.Request) {
	owner := a.uid(w, r)
	if owner == "" {
		return
	}
	var q struct {
		Reason string `json:"reason"`
	}
	if decode(r, &q) != nil {
		fail(w, 400, "参数错误")
		return
	}
	q.Reason = strings.TrimSpace(q.Reason)
	if len([]rune(q.Reason)) > 200 {
		fail(w, 400, "取消原因太长")
		return
	}
	err := a.db.update(func(d *Data) error {
		o, ok := d.Orders[r.PathValue("id")]
		if !ok || o.Owner != owner {
			return errNotFound
		}
		if o.Status != "awaiting_payment" {
			return errors.New("只有待付款订单可取消")
		}
		if o.PaymentStartedAt != "" {
			return errors.New("该订单已发起微信支付，请等待支付状态确认后联系客服处理取消")
		}
		o.Status = "canceled"
		o.CancelReason = q.Reason
		o.History = append(o.History, OrderHistory{At: now(), Actor: "customer", Action: "canceled", Note: q.Reason})
		recordAudit(d, "customer", "order.canceled", o.ID, "")
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
	reply(w, 200, map[string]string{"status": "canceled"})
}
