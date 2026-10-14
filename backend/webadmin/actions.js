// 运营操作层：模型上传、订单发货、物流和人员管理。
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
