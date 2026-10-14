// 结构与格式守卫：防止后续把后台重新写成大单文件。
const fs=require('fs')
const path=require('path')
const assert=require('assert')
const {spawnSync}=require('child_process')
const root=path.resolve(__dirname,'..')
const read=p=>fs.readFileSync(path.join(root,p),'utf8')
const exists=p=>fs.existsSync(path.join(root,p))

const modules=[
 'backend/main.go',
 'backend/http_helpers.go',
 'backend/media.go',
 'backend/work_handlers.go',
 'backend/checkout.go',
 'backend/logistics.go',
 'backend/order_lifecycle.go',
 'backend/users_admin.go',
 'backend/webadmin/index.html',
 'backend/webadmin/app.css',
 'backend/webadmin/core.js',
 'backend/webadmin/views.js',
 'backend/webadmin/actions.js',
 'miniprogram/utils/shipping.js',
 'miniprogram/utils/order.js'
]
for(const name of modules)assert(exists(name),'缺少模块：'+name)
assert(!exists('backend/operations.go'),'旧业务大文件不能与新模块并存')
assert(!exists('admin/index.html'),'后台只能有一套 HTML 源码')

const main=read('backend/main.go')
assert(!main.includes('func (a *App) createWork('),'启动文件混入了作品业务')
assert(!main.includes('func (a *App) createOrder('),'启动文件混入了下单业务')
assert(!main.includes('func (a *App) adminTrackingRefresh('),'启动文件混入了物流业务')

const html=read('backend/webadmin/index.html')
for(const file of ['core.js','views.js','actions.js','app.css']){
 assert(html.includes('/admin/'+file),'后台未加载分层资源：'+file)
}
for(const js of ['core.js','views.js','actions.js']){
 new Function(read('backend/webadmin/'+js))
}
const checkout=read('miniprogram/pages/preview/index.js')
const order=read('miniprogram/pages/order/index.js')
assert(checkout.includes("require('../../utils/shipping')"),'结算页必须复用收货地址模块')
assert(order.includes("require('../../utils/shipping')"),'订单详情必须复用收货地址模块')
assert(order.includes("require('../../utils/order')"),'订单详情必须复用状态格式模块')
assert(read('miniprogram/pages/orders/index.js').includes("require('../../utils/order')"),'订单列表必须复用状态格式模块')
for(const file of ['miniprogram/pages/works/index.wxml','miniprogram/utils/identity.js']){
 assert(!read(file).includes('当前为共用调试身份'),'调试说明不应出现在用户界面')
}
const files=fs.readdirSync(path.join(root,'backend')).filter(f=>f.endsWith('.go')).map(f=>path.join(root,'backend',f))
const formatted=spawnSync('gofmt',['-l',...files],{encoding:'utf8'})
assert.strictEqual(formatted.status,0,'gofmt 检查失败：'+formatted.stderr)
assert.strictEqual(formatted.stdout.trim(),'','Go 文件未运行 gofmt：'+formatted.stdout)
console.log('代码结构、去重约束与 gofmt 检查通过。')
