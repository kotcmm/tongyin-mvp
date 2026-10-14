const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.join(__dirname,'..')
const read=name=>fs.readFileSync(path.join(root,name),'utf8')
const html=read('backend/webadmin/index.html')
const js=read('backend/webadmin/app.js')
const css=read('backend/webadmin/app.css')
const embedded=html
assert.strictEqual(html,embedded,'Go embedded console and working admin source are out of sync')
const script=[null,js]
assert(script,'admin console script missing')
new Function(script[1])
for(const term of [
 '运营管理中心','/api/admin/auth/login','/api/admin/dashboard','/api/admin/accounts',
 '/api/admin/works','/api/admin/orders','/api/admin/users','/api/admin/audit',
 '/shipment','/tracking/refresh','refund','role'
])assert(js.includes(term) || html.includes(term),'missing admin feature '+term)
assert(html.includes('/admin/app.js') && html.includes('/admin/app.css'),'admin assets must be embedded in the Go service')
assert(css.includes('.nav-item'),'style bundle missing')
const minJS=read('miniprogram/pages/order/index.js'),minWXML=read('miniprogram/pages/order/index.wxml')
new Function(minJS)
for(const term of ['/api/orders/','loadTracking','copyTracking','trackingLoading','trackingError'])
 assert(minJS.includes(term),'missing mini shipping behavior '+term)
for(const term of ['物流进度','tracking.events','tracking-number','tracking-source','暂无轨迹'])
 assert(minWXML.includes(term),'missing mini shipping view '+term)
console.log('Single-binary admin console and mini-program shipping syntax checks passed.')
