// Verify login visibility uses authenticated /api/me rather than a cached token.
// This runner works without the WeChat runtime.
const assert=require('assert')
const fs=require('fs')
const vm=require('vm')
const path=require('path')
const root=path.join(__dirname,'..')
const read=p=>fs.readFileSync(path.join(root,p),'utf8')
const idSource=read('miniprogram/utils/identity.js')
const homeSource=read('miniprogram/pages/home/index.js')
const worksSource=read('miniprogram/pages/works/index.js')
const homeXML=read('miniprogram/pages/home/index.wxml')
const worksXML=read('miniprogram/pages/works/index.wxml')
const account={id:'abcdef1234567890abcdef12',created_at:'2026-10-14T09:00:00Z',works:2,orders:1}
const calls=[]
let error=null
function identityInMode(dev){
 const mod={exports:{}}
 vm.runInNewContext(idSource,{
  module:mod,
  require:arg=>{
   if(arg==='../config')return {DEV_LOGIN:dev}
   if(arg==='./api')return {request:async(method,route)=>{
    calls.push(route)
    if(error)throw error
    return account
   }}
   throw Error('unexpected '+arg)
  }
 })
 return mod.exports
}
let home
function fakePage(source,idService){
 let page
 vm.runInNewContext(source,{
  Page:obj=>page=obj,
  require:arg=>{
   if(arg.includes('utils/identity'))return idService
   if(arg.includes('utils/api'))return {request:async()=>[],tip(){}}
   throw Error('unexpected import '+arg)
  },wx:{},setTimeout,clearTimeout
 })
 return Object.assign({},page,{
  data:{...page.data},setData(delta){Object.assign(this.data,delta)}
 })
}
;(async()=>{
 const prod=identityInMode(false),dev=identityInMode(true)
 let result=await prod.loadIdentity()
 assert.equal(result.label,'微信已登录')
 assert.equal(result.mode,'wechat')
 assert.equal(result.accountCode,'TY-ABCDEF12')
 assert.equal('description' in result,false)
 result=await dev.loadIdentity()
 assert.equal(result.label,'测试账号')
 assert.equal(result.mode,'dev')
 assert.equal('description' in result,false)
 assert.deepEqual(calls,['/api/me','/api/me'])
 assert(homeXML.includes('identityState'),'home login UI must respond to identity state')
 assert(!worksXML.includes('identity-card')&&!worksXML.includes('identityCode'),'作品页不应出现账号信息卡片')
 assert(!worksSource.includes('refreshIdentity')&&!worksSource.includes('utils/identity'),'作品页不应重复请求账号状态')
 assert(homeXML.includes('accountAction'),'home identity entry missing')
 home=fakePage(homeSource,prod)
 await home.refreshIdentity()
 assert.equal(home.data.identityState,'wechat')
 assert.equal(home.data.identityLabel,'微信已登录')
 error=new Error('backend unavailable')
 await home.refreshIdentity()
 assert.equal(home.data.identityState,'error')
 assert.equal(home.data.identityLabel,'账号连接失败')
 error=null
 const invalid=identityInMode(false)
 const oldAccount=account.id
 account.id=''
 await assert.rejects(invalid.loadIdentity(),/账号验证失败/)
 account.id=oldAccount
 assert(!homeXML.includes('wx.getUserProfile')&&!worksXML.includes('wx.getUserProfile'),'profile authorization must be explicit')
 console.log('WeChat identity indicator: server verified, dev mode distinguished, errors do not claim login.')
})().catch(e=>{console.error(e);process.exitCode=1})
