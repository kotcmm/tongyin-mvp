// 页面渲染层：页面结构与内容展示，不直接修改业务数据。
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
