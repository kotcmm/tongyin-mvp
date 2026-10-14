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
let home,works
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
 for(const source of [homeXML,worksXML])assert(source.includes('identityState'),'login UI must respond to identity state')
 assert(homeXML.includes('accountAction'),'home identity entry missing')
 assert(worksXML.includes('identityCode'),'works account code missing')
 home=fakePage(homeSource,prod)
 await home.refreshIdentity()
 assert.equal(home.data.identityState,'wechat')
 assert.equal(home.data.identityLabel,'微信已登录')
 works=fakePage(worksSource,dev)
 await works.refreshIdentity()
 assert.equal(works.data.identityState,'dev')
 assert.equal(works.data.identityCode,'TY-ABCDEF12')
 error=new Error('backend unavailable')
 await works.refreshIdentity()
 assert.equal(works.data.identityState,'error')
 assert.equal(works.data.identityCode,'','do not show a stale account when verification fails')
 error=null
 const invalid=identityInMode(false)
 const oldAccount=account.id
 account.id=''
 await assert.rejects(invalid.loadIdentity(),/账号验证失败/)
 account.id=oldAccount
 assert(!homeXML.includes('wx.getUserProfile')&&!worksXML.includes('wx.getUserProfile'),'profile authorization must be explicit')
 console.log('WeChat identity indicator: server verified, dev mode distinguished, errors do not claim login.')
})().catch(e=>{console.error(e);process.exitCode=1})
