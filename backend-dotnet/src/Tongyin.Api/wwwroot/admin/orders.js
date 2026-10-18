'use strict';
// .NET 履约后台：仅提供当前已经迁移的订单制作与发货能力，避免出现旧 Go 页面上的失效操作。
const $ = (id) => document.getElementById(id);
const h = (value) => String(value ?? '').replace(/[&<>"']/g, (c) =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const statusLabels = {
  awaiting_payment: '待付款', paid: '已支付', printing: '制作中',
  shipped: '已发货', completed: '已完成', canceled: '已取消'
};
const state = { token: '', tab: 'orders', orders: [], page: 1, pageSize: 12, busy: false };

async function api(path, options = {}) {
  const headers = { 'X-Admin-Token': state.token, ...(options.headers || {}) };
  const response = await fetch(path, { ...options, headers, cache: 'no-store', credentials: 'omit' });
  const data = await response.json().catch(() => ({ error: '服务器响应格式异常' }));
  if (!response.ok) {
    const error = new Error(data.error || ('请求失败 HTTP ' + response.status));
    error.status = response.status;
    throw error;
  }
  return data;
}

function toast(message) {
  $('toast').textContent = message;
  $('toast').classList.remove('hidden');
  window.setTimeout(() => $('toast').classList.add('hidden'), 3500);
}

function logout() {
  state.token = '';
  state.orders = [];
  $('adminToken').value = '';
  $('app').classList.add('hidden');
  $('loginWrap').classList.remove('hidden');
  $('modalContainer').replaceChildren();
  $('adminToken').focus();
}

function setBusy(value) {
  state.busy = value;
  $('refresh').disabled = value;
  $('loginSubmit').disabled = value;
}

async function reload() {
  if (!state.token || state.busy) return;
  setBusy(true);
  $('pageMessage').textContent = '正在读取最新订单…';
  try {
    const orders = await api('/api/admin/orders');
    if (!Array.isArray(orders)) throw new Error('订单列表响应格式异常');
    state.orders = orders;
    $('lastLoaded').textContent = new Date().toLocaleTimeString();
    $('pageMessage').textContent = '';
    render();
  } catch (error) {
    $('pageMessage').textContent = error.message;
    if (error.status === 401) logout();
    throw error;
  } finally {
    setBusy(false);
  }
}

async function login(event) {
  event.preventDefault();
  $('loginError').textContent = '';
  state.token = $('adminToken').value.trim();
  if (!state.token) return;
  try {
    await reload();
    $('adminToken').value = '';
    $('loginWrap').classList.add('hidden');
    $('app').classList.remove('hidden');
  } catch (error) {
    $('loginError').textContent = error.status === 401 ? '后台令牌无效或服务端未配置 ADMIN_TOKEN' : error.message;
    state.token = '';
  }
}

function filteredOrders() {
  const keyword = $('keyword').value.trim().toLowerCase();
  const chosenStatus = $('statusFilter').value;
  return state.orders.filter((o) => {
    if (state.tab === 'shipping' && !['printing', 'shipped', 'completed'].includes(o.status)) return false;
    if (chosenStatus && o.status !== chosenStatus) return false;
    const searchable = [o.id, o.work_id, o.name, o.carrier, o.tracking]
      .map((value) => String(value ?? '')).join(' ').toLowerCase();
    return !keyword || searchable.includes(keyword);
  });
}

function render() {
  const rows = filteredOrders();
  const pageCount = Math.max(1, Math.ceil(rows.length / state.pageSize));
  state.page = Math.min(state.page, pageCount);
  const begin = (state.page - 1) * state.pageSize;
  $('listResults').innerHTML = rows.length
    ? rows.slice(begin, begin + state.pageSize).map(orderCard).join('')
    : '<div class="note">暂无符合条件的订单。</div>';
  $('pagerText').textContent = rows.length + ' 条 · 第 ' + state.page + '/' + pageCount + ' 页';
  $('previous').disabled = state.page <= 1;
  $('next').disabled = state.page >= pageCount;
}

function orderCard(o) {
  const id = h(o.id);
  const status = h(statusLabels[o.status] || o.status);
  const refund = o.refund_status
    ? '<span class="pill warning">退款处理中：' + h(o.refund_status) + '</span>' : '';
  let actions = '';
  if (o.status === 'paid' && !o.refund_status)
    actions = '<button type="button" data-action="printing" data-id="' + id + '">开始制作</button>';
  else if (o.status === 'printing' && !o.refund_status)
    actions = '<button type="button" data-action="shipment" data-id="' + id + '">录入发货</button>';
  else if (o.refund_status)
    actions = '<span class="small muted">存在退款申请，已暂停制作与发货</span>';
  const created = h(String(o.created_at || '').replace('T', ' ').slice(0, 19));
  const price = Number.isFinite(Number(o.price_fen)) ? (Number(o.price_fen) / 100).toFixed(2) : '--';
  const tracking = o.tracking
    ? '<div class="detail">物流：' + h(o.carrier || '快递') + ' · ' + h(o.tracking) +
      ' · ' + h(o.tracking_state || '尚未核实轨迹') + '</div>' : '';
  return '<article class="card"><div class="card-header"><div><strong>订单 #' + id +
    '</strong>　<span class="pill">' + status + '</span> ' + refund +
    '</div><strong>¥' + price + '</strong></div>' +
    '<p class="meta">作品 ' + h(o.work_id) + ' · ' + created + '</p>' +
    '<div class="detail">收件人：' + h(o.name) + '　' + h(o.phone) +
    '<div>地址：' + h(o.address) + '</div></div>' + tracking +
    '<div class="row" style="margin-top:14px">' + actions + '</div></article>';
}

function openTab(tab) {
  if (!['orders', 'shipping'].includes(tab)) return;
  state.tab = tab;
  state.page = 1;
  $('keyword').value = '';
  $('statusFilter').value = '';
  $('pageTitle').textContent = tab === 'shipping' ? '物流发货' : '全部订单';
  $('pageIntro').textContent = tab === 'shipping' ? '制作中、已发货及已完成订单' : '收款后制作 → 核实运单 → 发货';
  document.querySelectorAll('[data-tab]').forEach((node) =>
    node.classList.toggle('active', node.dataset.tab === tab));
  render();
}

async function requestChange(path, body) {
  if (state.busy) return;
  setBusy(true);
  try {
    await api(path, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    toast('操作成功，正在刷新订单');
  } finally {
    setBusy(false);
  }
  await reload();
}

function openShipment(id) {
  // 所有动态数据先经过 HTML 转义；订单 ID 仅从服务器查询结果取得。
  $('modalContainer').innerHTML = '<div class="modal-back"><form id="shipmentForm" class="modal">' +
    '<div class="card-header"><h2>录入真实物流发货</h2><button type="button" class="ghost" data-close>关闭 ✕</button></div>' +
    '<p class="small muted">订单 ' + h(id) + '。仅制作中订单可发货；请核对快递公司和实际运单号。</p>' +
    '<label for="carrier">快递公司</label><input id="carrier" name="carrier" required placeholder="例如 顺丰速运">' +
    '<label for="carrierCode">快递100公司编码</label><input id="carrierCode" name="carrierCode" required placeholder="例如 shunfeng">' +
    '<label for="tracking">真实运单号</label><input id="tracking" name="tracking" required placeholder="例如 SF12345678901">' +
    '<div class="row" style="margin-top:20px"><button type="submit">确认发货</button></div></form></div>';
  $('shipmentForm').dataset.orderId = id;
  $('carrier').focus();
}

async function handleListClick(event) {
  const button = event.target.closest('button[data-action]');
  if (!button || !event.currentTarget.contains(button) || state.busy) return;
  const id = button.dataset.id;
  if (button.dataset.action === 'shipment') {
    openShipment(id);
    return;
  }
  if (button.dataset.action === 'printing' && window.confirm('已经核对微信收款并确认开始制作？')) {
    try {
      await requestChange('/api/admin/orders/' + encodeURIComponent(id) + '/status', { status: 'printing' });
    } catch (error) {
      toast(error.message);
      if (error.status === 401) logout();
    }
  }
}

async function ship(event) {
  if (event.target.id !== 'shipmentForm') return;
  event.preventDefault();
  if (!window.confirm('确认快递单号真实有效，并将订单标记为已发货？')) return;
  const form = event.target;
  const id = form.dataset.orderId;
  const body = {
    carrier: $('carrier').value.trim(),
    carrier_code: $('carrierCode').value.trim(),
    tracking: $('tracking').value.trim()
  };
  try {
    await requestChange('/api/admin/orders/' + encodeURIComponent(id) + '/shipment', body);
    $('modalContainer').replaceChildren();
  } catch (error) {
    toast(error.message);
    if (error.status === 401) logout();
  }
}

document.addEventListener('DOMContentLoaded', () => {
  $('loginForm').addEventListener('submit', login);
  $('logout').addEventListener('click', logout);
  $('refresh').addEventListener('click', () => reload().catch((error) => toast(error.message)));
  $('keyword').addEventListener('input', () => { state.page = 1; render(); });
  $('statusFilter').addEventListener('change', () => { state.page = 1; render(); });
  $('previous').addEventListener('click', () => { if (state.page > 1) { state.page--; render(); } });
  $('next').addEventListener('click', () => { state.page++; render(); });
  document.querySelectorAll('[data-tab]').forEach((node) =>
    node.addEventListener('click', () => openTab(node.dataset.tab)));
  $('listResults').addEventListener('click', handleListClick);
  $('modalContainer').addEventListener('submit', ship);
  $('modalContainer').addEventListener('click', (event) => {
    if (event.target.matches('[data-close]') || event.target.classList.contains('modal-back'))
      $('modalContainer').replaceChildren();
  });
});
