// Current mini-program account identity, verified by the Go API.
// Never infer login status from a cached token or claim to know a WeChat nickname.
const {DEV_LOGIN}=require('../config')
const {request}=require('./api')

async function loadIdentity(){
 const account=await request('GET','/api/me')
 if(!account || typeof account.id!=='string' || !/^[a-f0-9]{24}$/.test(account.id)){
  throw new Error('账号验证失败，请重试')
 }
 return {
  mode:DEV_LOGIN?'dev':'wechat',
  label:DEV_LOGIN?'开发测试账号':'微信已登录',
  description:DEV_LOGIN?'当前为共用调试身份，不代表实际微信账号':'作品、订单与收货地址均归属于当前微信账号',
  accountCode:'TY-'+account.id.slice(0,8).toUpperCase(),
  workCount:typeof account.works==='number'?account.works:0,
  orderCount:typeof account.orders==='number'?account.orders:0
 }
}
module.exports={loadIdentity}
