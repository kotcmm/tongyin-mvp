const { upload, request, tip } = require('../../utils/api')
Page({
  data: { path: '', busy: false },
  choose() {
    wx.chooseMedia({
      count: 1,
      mediaType: ['image'],
      sourceType: ['album', 'camera'],
      success: (r) => this.setData({ path: r.tempFiles[0].tempFilePath })
    })
  },
  async send() {
    if (!this.data.path || this.data.busy) return
    this.setData({ busy: true })
    wx.showLoading({ title: '正在上传...' })
    try {
      const w = await upload('/api/works', this.data.path)
      await request('POST', '/api/works/' + w.id + '/generate', {})
      wx.navigateTo({ url: '/pages/choose/index?id=' + w.id })
    } catch (e) {
      tip(e)
    } finally {
      wx.hideLoading()
      this.setData({ busy: false })
    }
  },
  toWorks() {
    wx.switchTab({ url: '/pages/works/index' })
  }
})
