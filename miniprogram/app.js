const { API_BASE, DEV_LOGIN } = require('./config')
App({
  globalData: { apiBase: API_BASE, token: '' },
  onLaunch() { this.globalData.token = wx.getStorageSync('ty_token') || '' },
  async ensureLogin() {
    if (this.globalData.token) return this.globalData.token
    let code = 'DEV_LOGIN'
    if (!DEV_LOGIN) {
      const res = await new Promise((resolve, reject) => wx.login({ success: resolve, fail: reject }))
      code = res.code
    }
    const res = await new Promise((resolve, reject) => wx.request({
      url: API_BASE + '/api/auth/wechat', method:'POST', data:{ code },
      success: resolve, fail: reject
    }))
    if (res.statusCode !== 200 || !res.data.token) throw new Error(res.data?.error || '微信登录失败')
    this.globalData.token = res.data.token
    wx.setStorageSync('ty_token', res.data.token)
    return res.data.token
  }
})
