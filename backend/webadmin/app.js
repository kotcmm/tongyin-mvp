// 童印运营控制台：页面行为与状态管理。所有列表搜索均走后端分页接口。
const $ = (id) => document.getElementById(id),
  h = (s) =>
    String(s ?? '').replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]
    )
const labels = {
  image_pending: '待生成形象',
  image_ready: '形象待确认',
  model_pending: '待上传模型',
  model_ready: '3D模型已完成',
  failed: '处理失败',
  awaiting_payment: '待付款',
  paid: '已支付',
  printing: '制作中',
  shipped: '已发货',
  completed: '已完成',
  canceled: '已取消'
}
const names = {
  dashboard: ['概览', '运营数据与待办'],
  works: ['作品生产', '原画、形象图与 GLB 模型'],
  orders: ['全部订单', '支付、生产、取消及退款'],
  shipping: ['物流管理', '待发货、运单与物流轨迹'],
  users: ['微信用户', '用户的作品与订单归属'],
  accounts: ['管理人员', '角色权限和管理员账号'],
  audit: ['操作记录', '敏感后台操作追溯']
}
const state = {
  token: sessionStorage.getItem('ty_admin_session') || '',
  legacy: sessionStorage.getItem('ty_admin_legacy') || '',
  role: '',
  username: '',
  tab: 'dashboard',
  q: '',
  filter: '',
  page: 1,
  perPage: 12,
  total: 0,
  searchSeq: 0,
  searchTimer: null,
  works: [],
  orders: [],
  users: [],
  accounts: []
}
let toastTimer
function toast(message) {
  $('toast').textContent = message
  $('toast').classList.remove('hidden')
  clearTimeout(toastTimer)
  toastTimer = setTimeout(() => $('toast').classList.add('hidden'), 3600)
}
async function api(path, options = {}) {
  const headers = { ...(options.headers || {}) }
  if (state.token) headers.Authorization = 'Bearer ' + state.token
  else if (state.legacy) headers['X-Admin-Token'] = state.legacy
  const response = await fetch(path, { ...options, headers, cache: 'no-store' })
  const data = await response.json().catch(() => ({ error: '服务器未返回 JSON' }))
  if (!response.ok) {
    if (response.status === 401 && path !== '/api/admin/auth/login') {
      showLogin()
    }
    const error = new Error(data.error || '请求失败 HTTP ' + response.status)
    error.status = response.status
    throw error
  }
  return data
}
function jsonPost(path, data, method = 'POST') {
  return api(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data)
  })
}
function showLogin() {
  state.token = ''
  state.legacy = ''
  sessionStorage.removeItem('ty_admin_session')
  sessionStorage.removeItem('ty_admin_legacy')
  $('app').classList.add('hidden')
  $('loginWrap').classList.remove('hidden')
}
async function login() {
  $('loginError').textContent = ''
  $('loginSubmit').disabled = true
  try {
    const result = await api('/api/admin/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: $('username').value.trim(), password: $('password').value })
    })
    state.token = result.token
    state.legacy = ''
    sessionStorage.setItem('ty_admin_session', result.token)
    sessionStorage.removeItem('ty_admin_legacy')
    $('password').value = ''
    await start()
  } catch (e) {
    $('loginError').textContent = e.message
  } finally {
    $('loginSubmit').disabled = false
  }
}
async function legacyLogin() {
  state.legacy = $('legacy').value.trim()
  state.token = ''
  if (!state.legacy) {
    toast('请填写后台密钥')
    return
  }
  try {
    await api('/api/admin/me')
    sessionStorage.setItem('ty_admin_legacy', state.legacy)
    sessionStorage.removeItem('ty_admin_session')
    await start()
  } catch (e) {
    state.legacy = ''
    toast(e.message)
  }
}
function logout() {
  showLogin()
  $('body').innerHTML = ''
}
function showModal(content) {
  $('modalContainer').innerHTML =
    '<div class="modal-back" onclick="if(event.target===this)closeModal()"><div class="modal">' +
    content +
    '</div></div>'
}
function closeModal() {
  $('modalContainer').innerHTML = ''
}
function nav() {
  const tabs = [
    'dashboard',
    'works',
    'orders',
    'shipping',
    'users',
    ...(state.role === 'owner' ? ['accounts', 'audit'] : [])
  ]
  const icons = {
    dashboard: '◈',
    works: '▣',
    orders: '▤',
    shipping: '◇',
    users: '♙',
    accounts: '⚙',
    audit: '≡'
  }
  $('nav').innerHTML = tabs
    .map(
      (t) =>
        '<div class="nav-item ' +
        (state.tab === t ? 'active' : '') +
        '" onclick="openTab(\'' +
        t +
        '\')"><span class="glyph">' +
        icons[t] +
        '</span>' +
        names[t][0] +
        '</div>'
    )
    .join('')
}
async function start() {
  const me = await api('/api/admin/me')
  state.role = me.role
  state.username = me.username
  $('loginWrap').classList.add('hidden')
  $('app').classList.remove('hidden')
  $('accountName').textContent = me.username + ' · ' + me.role
  nav()
  await loadTab()
}
function openTab(tab) {
  clearTimeout(state.searchTimer)
  state.searchSeq++ // responses from a previous tab must never overwrite this one
  state.tab = tab
  state.q = ''
  state.filter = ''
  state.page = 1
  state.total = 0
  nav()
  loadTab()
}
function setSearch(s) {
  state.q = s
  state.page = 1
  state.searchSeq++ // invalidate any older response immediately, including debounce period
  clearTimeout(state.searchTimer)
  // Do not replace the toolbar; the input keeps focus and caret position.
  state.searchTimer = setTimeout(() => fetchListPage(), 300)
}
function setFilter(s) {
  state.filter = s
  state.page = 1
  clearTimeout(state.searchTimer)
  fetchListPage()
}
function pageBy(delta) {
  const pages = Math.max(1, Math.ceil(state.total / state.perPage))
  const next = Math.max(1, Math.min(pages, state.page + delta))
  if (next === state.page) return
  state.page = next
  fetchListPage()
}
function pager(total) {
  const pages = Math.max(1, Math.ceil(total / state.perPage))
  state.page = Math.min(state.page, pages)
  return (
    '<div class="pager"><span class="muted small">' +
    total +
    ' 条 · 第 ' +
    state.page +
    '/' +
    pages +
    ' 页</span><button class="ghost" onclick="pageBy(-1)" ' +
    (state.page === 1 ? 'disabled' : '') +
    '>上一页</button><button class="ghost" onclick="pageBy(1)" ' +
    (state.page === pages ? 'disabled' : '') +
    '>下一页</button></div>'
  )
}
function currentPage(rows) {
  const start = (state.page - 1) * state.perPage
  return rows.slice(start, start + state.perPage)
}
function status(s) {
  return (
    '<span class="pill ' +
    (['completed', 'model_ready', 'paid'].includes(s)
      ? 'done'
      : ['failed', 'canceled', 'awaiting_payment'].includes(s)
        ? 'warning'
        : '') +
    '">' +
    h(labels[s] || s) +
    '</span>'
  )
}
function date(s) {
  return h((s || '').replace('T', ' ').slice(0, 19))
}
async function loadTab() {
  if (!$('app') || $('app').classList.contains('hidden')) return
  clearTimeout(state.searchTimer)
  state.searchSeq++
  $('pageTitle').textContent = names[state.tab][0]
  $('pageIntro').textContent = names[state.tab][1]
  $('body').innerHTML = '<div class="panel muted">正在读取最新数据…</div>'
  try {
    if (state.tab === 'dashboard') await renderDashboard()
    else if (['works', 'orders', 'shipping', 'users'].includes(state.tab)) {
      setupListPage()
      await fetchListPage()
    } else if (state.tab === 'accounts') {
      state.accounts = await api('/api/admin/accounts')
      renderItems()
    } else if (state.tab === 'audit') {
      const data = await api('/api/admin/audit')
      renderAudit(data)
    }
    $('lastLoaded').textContent = new Date().toLocaleTimeString()
  } catch (e) {
    if (!['works', 'orders', 'shipping', 'users'].includes(state.tab)) {
      $('body').innerHTML =
        '<div class="panel">加载失败：' +
        h(e.message) +
        ' <button onclick="loadTab()">重试</button></div>'
    }
  }
}
function setupListPage() {
  const tab = state.tab
  const intro =
    tab === 'works'
      ? '原画 → AI 形象 → GLB。保留步骤式处理流程。'
      : tab === 'users'
        ? '使用内部用户编号，不公开微信 OpenID。'
        : '订单状态和收货信息以服务器为准，不跳过支付确认。'
  const statuses =
    tab === 'works'
      ? ['image_pending', 'image_ready', 'model_pending', 'model_ready', 'failed']
      : tab === 'shipping'
        ? ['printing', 'shipped', 'completed']
        : tab === 'orders'
          ? ['awaiting_payment', 'paid', 'printing', 'shipped', 'completed', 'canceled']
          : null
  $('body').innerHTML =
    '<div class="panel"><h3>' +
    h(names[tab][0]) +
    '</h3><p class="muted">' +
    h(intro) +
    '</p>' +
    toolbar(statuses) +
    '<div id="listResults" aria-live="polite"></div></div>'
}
async function fetchListPage() {
  const tab = state.tab
  if (!['works', 'orders', 'shipping', 'users'].includes(tab)) return
  // Delayed debounced calls must be invalidated on tab change / newer input.
  const seq = ++state.searchSeq
  const node = $('listResults')
  if (!node) return
  node.innerHTML = '<div class="note">正在查询服务器…</div>'
  const route = tab === 'works' ? 'works' : tab === 'users' ? 'users' : 'orders'
  const params = new URLSearchParams()
  params.set('page', String(state.page))
  params.set('page_size', String(state.perPage))
  if (state.q.trim()) params.set('q', state.q.trim())
  if (state.filter) params.set('status', state.filter)
  if (tab === 'shipping') params.set('category', 'shipping')
  try {
    const data = await api('/api/admin/search/' + route + '?' + params.toString())
    if (seq !== state.searchSeq || tab !== state.tab || !$('listResults')) return
    state.total = data.total
    state.page = data.page
    if (tab === 'works') state.works = data.items
    else if (tab === 'users') state.users = data.items
    else state.orders = data.items
    // Never replace the search toolbar while the user is typing.
    renderSearchResults()
    $('lastLoaded').textContent = new Date().toLocaleTimeString()
  } catch (e) {
    if (seq === state.searchSeq && tab === state.tab && $('listResults'))
      $('listResults').innerHTML =
        '<div class="note">查询失败：' +
        h(e.message) +
        ' <button class="ghost" onclick="fetchListPage()">重试</button></div>'
  }
}
function renderSearchResults() {
  if (state.tab === 'works') renderWorks()
  else if (state.tab === 'orders' || state.tab === 'shipping') renderOrders()
  else if (state.tab === 'users') renderUsers()
}
async function renderDashboard() {
  const res = await api('/api/admin/dashboard'),
    m = res.metrics || {}
  const stats = [
    ['用户数', m.users || 0],
    ['作品数', m.works || 0],
    ['待处理形象', m.image_pending || 0],
    ['等待模型', m.model_pending || 0],
    ['待付款', m.awaiting_payment || 0],
    ['已支付待制作', m.paid || 0],
    ['制作中', m.printing || 0],
    ['已发货', m.shipped || 0]
  ]
  $('body').innerHTML =
    '<div class="stats">' +
    stats
      .map((x) => '<div class="stat"><span>' + x[0] + '</span><b>' + x[1] + '</b></div>')
      .join('') +
    '</div>' +
    '<div class="panel"><h3>业务运行状态</h3><p>已支付订单金额（含未处理退款）　<strong>¥ ' +
    ((res.paid_gross_fen || 0) / 100).toFixed(2) +
    '</strong></p>' +
    '<p>微信支付：' +
    (res.pay_configured ? '已配置' : '尚未配置商户参数') +
    '　|　快递100：' +
    (res.tracking_configured ? '已配置查询凭证' : '尚未配置凭证') +
    '</p>' +
    '<div class="note">作品与订单归属同一个微信用户 ID。物流轨迹只有真实接口返回或人工核实录入才会显示；未开通微信支付/物流商户权限时，这两项不会伪装成已联通。</div>' +
    '<p style="margin-top:18px"><button onclick="openTab(\'works\')">处理作品</button> <button class="ghost" onclick="openTab(\'shipping\')">查看发货队列</button></p></div>'
}
function toolbar(statuses) {
  return (
    '<div class="toolbar"><input id="keyword" placeholder="搜索 ID、用户、订单、运单…" value="' +
    h(state.q) +
    '" oninput="setSearch(this.value)">' +
    (statuses
      ? '<select onchange="setFilter(this.value)"><option value="">全部状态</option>' +
        statuses
          .map(
            (s) =>
              '<option value="' +
              s +
              '" ' +
              (state.filter === s ? 'selected' : '') +
              '>' +
              h(labels[s] || s) +
              '</option>'
          )
          .join('') +
        '</select>'
      : '') +
    '</div>'
  )
}
function thumb(url) {
  return url
    ? '<a href="' +
        h(url) +
        '" target="_blank" rel="noreferrer"><img class="preview" loading="lazy" src="' +
        h(url) +
        '"></a>'
    : '<span class="muted small">待上传</span>'
}
function uploadRow(id, kind, title) {
  return (
    '<div class="upload"><span class="small muted">' +
    h(title) +
    '</span>' +
    '<input id="' +
    kind +
    '_' +
    h(id) +
    '" type="file" accept="' +
    (kind === 'model' ? '.glb' : 'image/png,image/jpeg,image/webp') +
    '">' +
    '<button onclick="uploadAsset(\'' +
    h(id) +
    "','" +
    kind +
    '\')">上传 / 替换</button></div>'
  )
}
function workCard(w) {
  const one = '<div class="step"><h4>① 原始画作</h4>' + thumb(w.original) + '</div>'
  const canImage = w.selected < 0 && ['image_pending', 'image_ready', 'failed'].includes(w.status)
  const two =
    '<div class="step"><h4>② AI 形象图</h4>' +
    thumb((w.candidates || [])[0]) +
    (canImage ? uploadRow(w.id, 'candidate', '一次一张形象图') : '<p>家长已确认此形象</p>') +
    '</div>'
  const canModel = w.selected >= 0 && ['model_pending', 'model_ready'].includes(w.status)
  const three =
    '<div class="step"><h4>③ GLB 白模</h4>' +
    (w.model
      ? '<a target="_blank" rel="noreferrer" href="' + h(w.model) + '">下载 3D 模型</a>'
      : '<p>尚无 GLB 模型</p>') +
    (canModel ? uploadRow(w.id, 'model', '上传 GLB 后手机可以旋转查看') : '') +
    '</div>'
  return (
    '<article class="card"><div class="card-header"><div><strong>作品 #' +
    h(w.id.slice(0, 12)) +
    '</strong>　' +
    status(w.status) +
    '</div><span class="meta">用户 ' +
    h(w.owner_user_id || '旧数据') +
    '　' +
    date(w.created_at) +
    '</span></div>' +
    '<div class="grid">' +
    one +
    two +
    three +
    '</div><div class="row" style="margin-top:15px"><input id="note_' +
    h(w.id) +
    '" placeholder="失败原因 / 处理备注" style="flex:1">' +
    '<button class="danger" onclick="changeWork(\'' +
    h(w.id) +
    "','failed')\">标记失败</button>" +
    (w.status === 'failed' && w.selected >= 0
      ? '<button class="ghost" onclick="changeWork(\'' +
        h(w.id) +
        "','model_pending')\">重新处理模型</button>"
      : '') +
    '</div></article>'
  )
}
function renderWorks() {
  const rows = state.works
  $('listResults').innerHTML =
    (rows.length ? rows.map(workCard).join('') : '<div class="note">暂无匹配的作品。</div>') +
    pager(state.total)
}
function orderRow(o) {
  let controls = ''
  if (o.status === 'awaiting_payment')
    controls =
      '<button class="danger" onclick="changeOrder(\'' +
      h(o.id) +
      "','canceled')\">取消未付款</button>"
  if (o.status === 'paid')
    controls = '<button onclick="changeOrder(\'' + h(o.id) + "','printing')\">开始制作</button>"
  if (o.status === 'printing')
    controls = '<button onclick="shipmentModal(\'' + h(o.id) + '\')">录入发货</button>'
  if (o.status === 'shipped')
    controls =
      '<button class="ghost" onclick="refreshTracking(\'' +
      h(o.id) +
      '\')">查询物流</button> <button class="ghost" onclick="manualTracking(\'' +
      h(o.id) +
      '\')">补录轨迹</button> <button onclick="changeOrder(\'' +
      h(o.id) +
      "','completed')\">完成订单</button>"
  if (['paid', 'printing'].includes(o.status) && !o.refund_status)
    controls +=
      ' <button class="danger" onclick="requestRefund(\'' + h(o.id) + '\')">记录退款申请</button>'
  const events = (o.tracking_events || [])
    .slice(0, 3)
    .map(
      (e) =>
        '<div class="event"><b>' +
        date(e.time) +
        '</b>　' +
        h(e.context) +
        ' <span class="muted small">(' +
        h(e.source || '手工') +
        ')</span></div>'
    )
    .join('')
  return (
    '<article class="card"><div class="card-header"><div><strong>订单 #' +
    h(o.id.slice(0, 12)) +
    '</strong>　' +
    status(o.status) +
    (o.refund_status
      ? '　<span class="pill warning">退款申请：' + h(o.refund_status) + '</span>'
      : '') +
    '</div><strong>¥' +
    (o.price_fen / 100).toFixed(2) +
    '</strong></div>' +
    '<div class="meta">用户 ' +
    h(o.owner_user_id || '未知') +
    '　|　作品 ' +
    h(o.work_id) +
    '　|　' +
    date(o.created_at) +
    '</div>' +
    '<div class="detail">收件人：' +
    h(o.name) +
    '　' +
    h(o.phone) +
    '<br>地址：' +
    h(o.address) +
    (o.tracking
      ? '<br>物流：' +
        h(o.carrier || '快递') +
        ' · ' +
        h(o.tracking) +
        '　' +
        h(o.tracking_state || '尚无轨迹')
      : '') +
    '</div>' +
    (events ? '<div class="timeline">' + events + '</div>' : '') +
    '<div class="row" style="margin-top:14px">' +
    controls +
    '</div></article>'
  )
}
function renderOrders() {
  const rows = state.orders
  $('listResults').innerHTML =
    (rows.length ? rows.map(orderRow).join('') : '<div class="note">没有匹配的订单。</div>') +
    pager(state.total)
}
function renderUsers() {
  const rows = state.users
  $('listResults').innerHTML =
    '<div class="table-wrap"><table class="table"><tr><th>用户编号</th><th>作品</th><th>订单</th><th>创建时间</th><th>状态</th><th>操作</th></tr>' +
    rows
      .map(
        (u) =>
          '<tr><td>' +
          h(u.id) +
          '</td><td>' +
          u.works +
          '</td><td>' +
          u.orders +
          '</td><td>' +
          date(u.created_at) +
          '</td><td>' +
          status(u.disabled ? 'canceled' : 'completed') +
          '</td>' +
          '<td><button class="' +
          (u.disabled ? 'ghost' : 'danger') +
          '" onclick="toggleUser(\'' +
          h(u.id) +
          "'," +
          !u.disabled +
          ')">' +
          (u.disabled ? '启用' : '停用') +
          '</button></td></tr>'
      )
      .join('') +
    '</table></div>' +
    pager(state.total)
}
function renderAccounts() {
  $('body').innerHTML =
    '<div class="panel"><div class="card-header"><h3>运营后台账号</h3><button onclick="createStaff()">新增账号</button></div>' +
    '<div class="note">owner：全权限；operator：处理作品及订单；production：作品制作；shipping：发货物流；service：查询客服信息。修改账号会使旧会话失效。</div>' +
    '<div class="table-wrap"><table class="table"><tr><th>账号</th><th>角色</th><th>启用状态</th><th>创建时间</th><th>操作</th></tr>' +
    state.accounts
      .map(
        (a) =>
          '<tr><td>' +
          h(a.username) +
          '</td><td>' +
          h(a.role) +
          '</td><td>' +
          (a.disabled ? '停用' : '正常') +
          '</td><td>' +
          date(a.created_at) +
          '</td><td><button class="ghost" onclick="editStaff(\'' +
          h(a.username) +
          '\')">管理</button></td></tr>'
      )
      .join('') +
    '</table></div></div>'
}
function renderAudit(rows) {
  $('body').innerHTML =
    '<div class="panel"><h3>后台操作日志（最近300条）</h3>' +
    '<div class="table-wrap"><table class="table"><tr><th>时间</th><th>操作人</th><th>事件</th><th>对象</th><th>备注</th></tr>' +
    rows
      .map(
        (a) =>
          '<tr><td>' +
          date(a.created_at) +
          '</td><td>' +
          h(a.owner) +
          '</td><td>' +
          h(a.action) +
          '</td><td>' +
          h(a.target) +
          '</td><td>' +
          h(a.detail || '') +
          '</td></tr>'
      )
      .join('') +
    '</table></div></div>'
}
function renderItems() {
  if (state.tab === 'accounts') renderAccounts()
  else renderSearchResults()
}
async function uploadAsset(id, kind) {
  const f = $(kind + '_' + id)?.files?.[0]
  if (!f) {
    toast('请先选择文件')
    return
  }
  if (f.size > (kind === 'model' ? 100 : 10) * 1048576) {
    toast('文件超出上传限制')
    return
  }
  const form = new FormData()
  form.append('file', f)
  try {
    await api('/api/admin/works/' + id + '/asset?kind=' + kind, { method: 'POST', body: form })
    toast('上传完成')
    await loadTab()
  } catch (e) {
    toast(e.message)
  }
}
async function changeWork(id, status) {
  if (!confirm('确定修改这件作品的处理状态？')) return
  try {
    await jsonPost('/api/admin/works/' + id + '/status', {
      Status: status,
      Note: $('note_' + id)?.value || ''
    })
    toast('作品状态已更新')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
async function changeOrder(id, status) {
  if (!confirm('确定将订单状态调整为「' + (labels[status] || status) + '」？')) return
  try {
    await jsonPost('/api/admin/orders/' + id + '/status', { Status: status })
    toast('订单状态已更新')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
function shipmentModal(id) {
  showModal(
    '<div class="card-header"><h2>录入物流发货</h2><button class="ghost" onclick="closeModal()">关闭 ✕</button></div>' +
      '<p class="muted">仅制作中订单可以发货，系统会保存快递公司、运单号和发货时间。</p>' +
      '<label>快递公司</label><input id="carrier" placeholder="例如 顺丰速运">' +
      '<label>快递100公司编码</label><input id="carrierCode" placeholder="例如 shunfeng">' +
      '<label>快递单号</label><input id="number" placeholder="真实运单号码">' +
      '<div class="row" style="margin-top:25px"><button onclick="submitShipment(\'' +
      id +
      '\')">确认发货</button></div>'
  )
}
async function submitShipment(id) {
  if (!confirm('请核对真实运单号，确定标记已发货？')) return
  try {
    await jsonPost('/api/admin/orders/' + id + '/shipment', {
      carrier: $('carrier').value,
      carrier_code: $('carrierCode').value,
      tracking: $('number').value
    })
    closeModal()
    toast('发货信息已保存')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
async function refreshTracking(id) {
  try {
    const result = await jsonPost('/api/admin/orders/' + id + '/tracking/refresh', {})
    toast('已更新 ' + result.events.length + ' 条物流轨迹')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
function manualTracking(id) {
  showModal(
    '<div class="card-header"><h2>人工补录真实物流信息</h2><button class="ghost" onclick="closeModal()">关闭 ✕</button></div>' +
      '<p class="muted">仅填写已核实的信息，页面将明确标记为「人工录入」。</p>' +
      '<label>轨迹时间</label><input id="eventTime" placeholder="2026-10-14 16:20:00">' +
      '<label>物流信息</label><textarea rows="3" id="eventText" placeholder="如：快件已被签收"></textarea>' +
      '<div class="row" style="margin-top:25px"><button onclick="submitManual(\'' +
      id +
      '\')">保存记录</button></div>'
  )
}
async function submitManual(id) {
  try {
    await jsonPost('/api/admin/orders/' + id + '/tracking', {
      time: $('eventTime').value,
      context: $('eventText').value
    })
    closeModal()
    toast('轨迹已保存')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
async function requestRefund(id) {
  const reason = prompt('请输入退款原因。此操作只登记退款申请，不会发起微信资金退款。')
  if (!reason) return
  try {
    const v = await jsonPost('/api/admin/orders/' + id + '/refund', { reason })
    alert(v.message)
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
async function toggleUser(id, disabled) {
  if (!confirm(disabled ? '确定停用此用户？其小程序登录将受限。' : '重新启用此用户？')) return
  try {
    await jsonPost('/api/admin/users/' + id + '/status', { disabled })
    toast('用户状态已修改')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
const roles = ['owner', 'operator', 'production', 'shipping', 'service']
function roleOptions(selected) {
  return roles
    .map(
      (r) =>
        '<option value="' + r + '" ' + (selected === r ? 'selected' : '') + '>' + r + '</option>'
    )
    .join('')
}
function createStaff() {
  showModal(
    '<div class="card-header"><h2>新增管理人员</h2><button class="ghost" onclick="closeModal()">关闭 ✕</button></div>' +
      '<label>管理员账号</label><input id="staffName" autocomplete="off" placeholder="例如 printer01">' +
      '<label>登录密码（不少于12位）</label><input id="staffPassword" type="password" autocomplete="new-password">' +
      '<label>角色</label><select id="staffRole">' +
      roleOptions('production') +
      '</select>' +
      '<div class="row" style="margin-top:20px"><button onclick="saveNewStaff()">创建账号</button></div>'
  )
}
async function saveNewStaff() {
  try {
    await jsonPost('/api/admin/accounts', {
      username: $('staffName').value,
      password: $('staffPassword').value,
      role: $('staffRole').value
    })
    closeModal()
    toast('账号已创建')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
function editStaff(name) {
  const u = state.accounts.find((x) => x.username === name)
  if (!u) return
  showModal(
    '<div class="card-header"><h2>修改管理员 ' +
      h(name) +
      '</h2><button class="ghost" onclick="closeModal()">关闭 ✕</button></div>' +
      '<label>角色</label><select id="staffRole">' +
      roleOptions(u.role) +
      '</select>' +
      '<label>新密码（留空则不修改）</label><input type="password" id="staffPassword" autocomplete="new-password">' +
      '<label><input type="checkbox" style="width:auto" id="staffDisabled" ' +
      (u.disabled ? 'checked' : '') +
      '> 停用账号</label>' +
      '<div class="row" style="margin-top:20px"><button onclick="saveStaff(\'' +
      h(name) +
      '\')">保存修改</button></div>'
  )
}
async function saveStaff(name) {
  try {
    await jsonPost(
      '/api/admin/accounts/' + name,
      {
        role: $('staffRole').value,
        password: $('staffPassword').value,
        disabled: $('staffDisabled').checked
      },
      'PUT'
    )
    closeModal()
    toast('管理员已更新')
    loadTab()
  } catch (e) {
    toast(e.message)
  }
}
if (state.token || state.legacy) start().catch(() => showLogin())
