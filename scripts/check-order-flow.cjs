const fs=require('fs')
const path=require('path')
const vm=require('vm')
const assert=require('assert')
const root=path.resolve(__dirname,'..')
const read=p=>fs.readFileSync(path.join(root,p),'utf8')
const preview=read('miniprogram/pages/preview/index.js')
const previewXml=read('miniprogram/pages/preview/index.wxml')
const order=read('miniprogram/pages/order/index.js')
const orderXml=read('miniprogram/pages/order/index.wxml')
for(const marker of ["继续付款","查看订单","checkingOrder","existingOrder","loadExistingOrder","changeShipping"]){
 assert(previewXml.includes(marker),'preview missing '+marker)
}
for(const marker of ["saveShipping","startShippingEdit","chooseWeChatAddress","editingShipping"]){
 assert(order.includes(marker),'order JS missing '+marker)
}
for(const marker of ["修改地址","保存本订单地址","editingShipping","chooseWeChatAddress"]){
 assert(orderXml.includes(marker),'order UI missing '+marker)
}
const currentOrder={id:'order-a',work_id:'work-a',status:'awaiting_payment',name:'旧地址',phone:'13800138000',address:'广州测试路100号',price_fen:12900}
const calls=[],navigation=[]
let previewPage
const api=async(method,url,data)=>{
 calls.push({method,url,data})
 if(method==='GET' && url.includes('/works/work-a/order'))return {order:currentOrder}
 if(method==='GET' && url.includes('/orders/order-a'))return {...currentOrder}
 if(method==='PUT' && url.includes('/orders/order-a/shipping-address'))return {...currentOrder,...data}
 if(method==='GET'&&url==='/api/shipping-address')return {address:{name:'常用地址',phone:'13900139000',address:'广州市其他地方200号'}}
 throw new Error('unexpected call '+method+' '+url)
}
const wx={
 navigateTo({url}){navigation.push(url)},
 redirectTo({url}){navigation.push('redirect:'+url)},
 showToast(){},
 chooseAddress({success}){success({userName:'微信姓名',telNumber:'13900999000',provinceName:'广东省',cityName:'广州市',countyName:'海珠区',detailInfo:'新地址300号'})}
}
const req=name=>{
 if(name.endsWith('utils/api'))return {request:api,tip:e=>{throw e}}
 if(name.endsWith('utils/order'))return {formatOrder:x=>({...x,price:(x.price_fen/100).toFixed(2)})}
 if(name.endsWith('utils/shipping'))return {
  normalizeShipping:(name,phone,address)=>({name,phone,address}),
  chooseAddress:cb=>cb({name:'微信姓名',phone:'13900999000',address:'广东省广州市海珠区新地址300号'})
 }
 if(name.endsWith('/index'))return {createScopedThreejs(){}}
 return {registerGLTFLoader(){}}
}
vm.runInNewContext(preview,{Page:p=>previewPage=p,require:req,wx,setInterval(){},clearInterval(){}})
const p=Object.assign({},previewPage,{data:{id:'work-a',name:'',phone:'',address:'',shippingLoaded:false,existingOrder:null,checkingOrder:false},setData(patch,cb){Object.assign(this.data,patch);if(cb)cb()}})
;(async()=>{
 await p.loadExistingOrder()
 assert(p.data.existingOrder&&p.data.existingOrder.id==='order-a','not found')
 assert(p.data.name==='旧地址','should show order shipping snapshot instead of default')
 await p.loadShipping()
 assert(p.data.name==='旧地址','default address replaced the order shipping snapshot')
 await p.submit()
 assert(navigation.includes('/pages/order/index?id=order-a'),'continue pay should open existing order')
 assert(!calls.some(x=>x.method==='POST'&&x.url==='/api/orders'),'no duplicate order should be created')
 p.changeShipping()
 assert(navigation.includes('/pages/order/index?id=order-a&editAddress=1'),'order address edit should open order detail')
 let orderPage
 vm.runInNewContext(order,{
  Page:x=>orderPage=x,require:req,wx,
  setTimeout(fn){fn()}
 })
 const o=Object.assign({},orderPage,{data:{id:'order-a',item:null,editingShipping:false,savingShipping:false},setData(patch,cb){Object.assign(this.data,patch);if(cb)cb()}})
 o._editOnOpen=true
 await o.refresh()
 assert(o.data.editingShipping,'query edit should open editor')
 o.chooseWeChatAddress()
 assert(o.data.editAddress.includes('新地址300号'),'wx address not selected')
 await o.saveShipping()
 assert(calls.some(x=>x.method==='PUT'&&x.url==='/api/orders/order-a/shipping-address'),'did not save to order')
 assert(!o.data.editingShipping,'save did not close editor')
 console.log('Existing-order checkout and pending-order address edit checks passed.')
})().catch(e=>{console.error(e);process.exitCode=1})
