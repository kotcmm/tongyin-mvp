const { request, tip } = require('../../utils/api')
const { formatOrder } = require('../../utils/order')
Page({
  data: { items: [], loading: true, error: '' },
  onShow() {
    this.refresh()
  },
  onPullDownRefresh() {
    this.refresh().then(
      () => wx.stopPullDownRefresh(),
      () => wx.stopPullDownRefresh()
    )
  },
  async refresh() {
    try {
      const items = await request('GET', '/api/orders')
      items.sort((a, b) => (b.created_at || '').localeCompare(a.created_at || ''))
      this.setData({ items: items.map(formatOrder), loading: false, error: '' })
    } catch (e) {
      this.setData({ loading: false, error: e.message || '加载失败' })
      tip(e)
    }
  },
  open(e) {
    wx.navigateTo({ url: '/pages/order/index?id=' + e.currentTarget.dataset.id })
  },
  make() {
    wx.switchTab({ url: '/pages/home/index' })
  },
  toWorks() {
    wx.switchTab({ url: '/pages/works/index' })
  }
})
