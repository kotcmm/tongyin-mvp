// 控制台基础层：认证、导航、服务端搜索与分页状态。
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
  ai: ['AI 模型配置', '图片与 3D 提供商、默认模型及提示词'],
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
  accounts: [],
  aiProviders: [],
  aiPrompts: {}
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
    ...(state.role === 'owner' ? ['ai', 'accounts', 'audit'] : [])
  ]
  const icons = {
    dashboard: '◈',
    works: '▣',
    orders: '▤',
    shipping: '◇',
    users: '♙',
    ai: '✦',
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
    } else if (state.tab === 'ai') {
      await renderAIConfig()
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
