const fs = require('node:fs')
const path = require('node:path')
const vm = require('node:vm')
const assert = require('node:assert/strict')
const root = path.resolve(__dirname, '..')
const source = fs.readFileSync(path.join(root, 'miniprogram/pages/works/index.js'), 'utf8')
const wxml = fs.readFileSync(path.join(root, 'miniprogram/pages/works/index.wxml'), 'utf8')

assert.match(wxml, /catchtap="confirmDelete"/, '删除按钮必须阻止卡片点击事件冒泡')
assert.match(wxml, /data-id="{{work.id}}"/, '必须只删除对应的作品')
const wxss = fs.readFileSync(path.join(root, 'miniprogram/pages/works/index.wxss'), 'utf8')
const mainStart = wxml.indexOf('class="work-main"')
const detailStart = wxml.indexOf('class="work-detail"')
const actionsStart = wxml.indexOf('class="work-actions"')
assert.ok(mainStart > 0 && detailStart > mainStart && actionsStart > detailStart, '卡片主内容和底部操作区应明确分区')
assert.match(wxml, /<\/view>\s*<view class="work-actions"/, '操作区必须与卡片左右内容平级，不能挤压文字列')
assert.match(wxss, /\.work-card\s*\{[^}]*flex-direction:\s*column/, '作品卡片应上下分区，避免操作区挤窄文字')
assert.match(wxss, /\.work-main\s*\{[^}]*display:\s*flex/, '原画与详情应保持横向布局')
assert.match(wxss, /\.work-link\s*\{[^}]*white-space:\s*nowrap/, '查看进度必须保持一行显示')
assert.match(wxss, /\.work-delete\s*\{[^}]*width:\s*1[0-9]{2}rpx/, '删除按钮应有受控宽度，不能撑满卡片')

const calls = []
const messages = []
let modalConfirm = true
let rejectDelete = false
let page
const wx = {
  showModal(opts) { messages.push(opts); opts.success({ confirm: modalConfirm }) },
  showToast(opts) { messages.push(opts) },
  navigateTo(opts) { calls.push({ navigation: opts.url }) }
}
const works = [
  { id: 'w1', status: 'failed', created_at: '2026-10-19' },
  { id: 'w2', status: 'image_pending', created_at: '2026-10-18' },
  { id: 'w3', status: 'model_ready', created_at: '2026-10-17' }
]
const request = async (method, url) => {
  calls.push({ method, url })
  if (method === 'GET') return works.slice()
  if (rejectDelete) { const e = new Error('已有订单，不能删除'); e.statusCode = 409; throw e }
  assert.equal(method, 'DELETE')
  return {}
}
const errors = []
vm.runInNewContext(source, {
  Page(value) { page = value },
  require(name) {
    assert.ok(name.includes('utils/api'))
    return { request, tip(e) { errors.push(e.message) } }
  },
  wx
}, { filename: 'works/index.js' })
const p = Object.assign({}, page, {
  data: { ...page.data, all: works.slice(), items: works.slice(), ongoing: 1, loading: false },
  setData(patch) { Object.assign(this.data, patch) }
})
const event = (id) => ({ currentTarget: { dataset: { id } } })

;(async () => {
  modalConfirm = false
  p.confirmDelete(event('w1'))
  await Promise.resolve()
  assert.equal(calls.length, 0, '取消确认不能发送删除请求')

  modalConfirm = true
  p.confirmDelete(event('w1'))
  await new Promise((resolve) => setImmediate(resolve))
  assert.equal(calls.filter(c => c.method === 'DELETE').length, 1)
  assert.equal(calls.find(c => c.method === 'DELETE').url, '/api/works/w1')
  assert.equal(p.data.all.length, 2)
  assert.ok(!p.data.all.some(x => x.id === 'w1'))
  assert.equal(p.data.ongoing, 1)
  assert.equal(calls.some(c => c.navigation), false, '删除不应该跳转详情页')
  p.chooseFilter({ currentTarget: { dataset: { filter: 'ongoing' } } })
  assert.equal(p.data.items.length, 1)

  rejectDelete = true
  p.confirmDelete(event('w2'))
  await new Promise((resolve) => setImmediate(resolve))
  assert.equal(p.data.all.length, 2, '后端拒绝后原作品不能从列表丢失')
  assert.equal(p.data.deletingId, '')
  assert.ok(errors.includes('已有订单，不能删除'))
  console.log('Mini-program work deletion: cancel, success, filter, and rejection checks passed.')
})().catch(e => { console.error(e); process.exitCode = 1 })
