// 公共业务模块回归测试：结算页和订单详情共用同一套数据规则。
const fs=require('fs')
const vm=require('vm')
const path=require('path')
const assert=require('assert')
const root=path.join(__dirname,'..')
function loadModule(file,wx){
 const mod={exports:{}}
 vm.runInNewContext(fs.readFileSync(path.join(root,file),'utf8'),{module:mod,wx})
 return mod.exports
}
let choose
const messages=[]
const wx={
 chooseAddress(obj){choose=obj},
 showToast(x){messages.push(x.title)}
}
const shipping=loadModule('miniprogram/utils/shipping.js',wx)
const order=loadModule('miniprogram/utils/order.js',{})
assert.equal(shipping.normalizeShipping('张 三',' 13800138000 ',' 广州市海珠区测试路100号').phone,'13800138000')
assert.equal(shipping.normalizeShipping('张','1','广州'),null)
assert.equal(shipping.normalizeShipping('张三','13800138000','短地址'),null)
let selected
shipping.chooseAddress(data=>{selected=data})
choose.success({userName:'张三',telNumber:'13800138000',provinceName:'广东省',cityName:'广州市',countyName:'海珠区',detailInfo:'测试路100号'})
assert.equal(selected.address,'广东省广州市海珠区测试路100号')
choose.fail({errMsg:'chooseAddress:fail cancel'})
assert.equal(messages.length,0,'用户主动取消不得提示错误')
choose.fail({errMsg:'chooseAddress:fail unavailable'})
assert.equal(messages.length,1,'微信地址服务失败需要提示')
const result=order.formatOrder({id:'abcdef012345',status:'shipped',price_fen:12900,created_at:'2026-10-14T09:30:00'})
assert.equal(result.label,'已发货')
assert.equal(result.shortid,'abcdef01')
assert.equal(result.price,'129.00')
assert.equal(result.date,'2026-10-14 09:30')
assert.equal(order.ORDER_LABELS.awaiting_payment,'待付款')
console.log('Common shipping and order format helper checks passed.')
