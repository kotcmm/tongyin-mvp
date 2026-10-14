const { API_BASE, DEV_LOGIN } = require('./config')
const TOKEN_KEY = DEV_LOGIN ? 'ty_token_dev' : 'ty_token_prod'
App({
  globalData: { apiBase: API_BASE, token: '' },
  onLaunch() {
    this.globalData.token = wx.getStorageSync(TOKEN_KEY) || ''
  },
  clearLogin() {
    this.globalData.token = ''
    wx.removeStorageSync(TOKEN_KEY)
  },
  async ensureLogin() {
    if (this.globalData.token) return this.globalData.token
    if (this._loginPromise) return this._loginPromise
    this._loginPromise = (async () => {
      let code = 'DEV_LOGIN'
      if (!DEV_LOGIN) {
        const res = await new Promise((resolve, reject) =>
          wx.login({ success: resolve, fail: reject })
        )
        code = res.code
      }
      const res = await new Promise((resolve, reject) =>
        wx.request({
          url: API_BASE + '/api/auth/wechat',
          method: 'POST',
          data: { code },
          success: resolve,
          fail: reject
        })
      )
      if (res.statusCode !== 200 || !res.data || !res.data.token)
        throw new Error((res.data && res.data.error) || '微信登录失败')
      this.globalData.token = res.data.token
      wx.setStorageSync(TOKEN_KEY, res.data.token)
      return res.data.token
    })()
    try {
      return await this._loginPromise
    } finally {
      this._loginPromise = null
    }
  }
})
