// 账户状态以服务端 /api/me 验证结果为准，不使用本地缓存推断登录成功。
const { DEV_LOGIN } = require('../config')
const { request } = require('./api')

async function loadIdentity() {
  const account = await request('GET', '/api/me')
  if (!account || typeof account.id !== 'string' || !/^[a-f0-9]{24}$/.test(account.id)) {
    throw new Error('账号验证失败，请重试')
  }
  return {
    mode: DEV_LOGIN ? 'dev' : 'wechat',
    label: DEV_LOGIN ? '测试账号' : '微信已登录',
    accountCode: 'TY-' + account.id.slice(0, 8).toUpperCase()
  }
}
module.exports = { loadIdentity }
