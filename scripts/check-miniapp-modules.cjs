// 微信小程序模块完整性检查：弥补仅执行 node --check 无法发现缺失 require 的问题。
const assert = require('assert')
const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '../miniprogram')
const config = JSON.parse(fs.readFileSync(path.join(root, 'project.config.json'), 'utf8'))
const app = JSON.parse(fs.readFileSync(path.join(root, 'app.json'), 'utf8'))

// 开发工具过滤无依赖文件时，可能误把公共工具模块排除在编译包之外。
assert.strictEqual(config.setting.ignoreDevUnusedFiles, false, '开发环境不能过滤公共模块')
assert.strictEqual(config.setting.ignoreUploadUnusedFiles, false, '上传时不能误过滤公共模块')
assert(
  (config.packOptions.include || []).some(item =>
    item.type === 'folder' && item.value.replace(/\\/g, '/') === 'utils'
  ),
  '必须显式保留 utils 文件夹'
)

const rootJS = path.join(root, 'app.js')
const entrypoints = [rootJS]
for (const page of app.pages) {
  const candidate = path.join(root, page + '.js')
  assert(fs.existsSync(candidate), '未注册或缺少的页面：' + page)
  entrypoints.push(candidate)
}

const visited = new Set()
function checkModule(file) {
  const canonical = path.resolve(file)
  assert(
    canonical.startsWith(root + path.sep),
    '模块路径越过小程序根目录：' + canonical
  )
  assert(fs.existsSync(canonical), 'require 指向不存在的文件：' + canonical)
  if (visited.has(canonical)) return
  visited.add(canonical)

  const src = fs.readFileSync(canonical, 'utf8')
  const imports = [...src.matchAll(/\brequire\s*\(\s*['"]([^'"]+)['"]\s*\)/g)]
  for (const [,specifier] of imports) {
    if (!specifier.startsWith('.')) continue
    const resolved = path.resolve(path.dirname(canonical), specifier)
    const candidates = [resolved, resolved + '.js', path.join(resolved, 'index.js')]
    const target = candidates.find(p => fs.existsSync(p) && fs.statSync(p).isFile())
    assert(target, '缺失模块：' + specifier + '，引用文件：' + path.relative(root, canonical))
    checkModule(target)
  }
}
for (const file of entrypoints) checkModule(file)

// 新增的订单/收货/账号工具必须同时存在，并能被普通 CommonJS 运行时加载。
for (const name of ['api', 'order', 'shipping', 'identity']) {
  assert(visited.has(path.join(root, 'utils', name + '.js')), name + '.js 未被任何入口引用')
}
const order = require(path.join(root, 'utils/order.js'))
const shipping = require(path.join(root, 'utils/shipping.js'))
assert.strictEqual(typeof order.formatOrder, 'function')
assert.strictEqual(typeof shipping.normalizeShipping, 'function')
assert.strictEqual(typeof shipping.chooseAddress, 'function')

console.log('小程序模块检查通过：' + visited.size + ' 个依赖模块路径均存在，公共模块已纳入打包配置。')
