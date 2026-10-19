const { request, tip } = require('../../utils/api')
const STATES = {
  image_pending: {
    title: '等待形象制作',
    hint: '作品已收到，等待处理',
    step: 1,
    tone: 'processing'
  },
  image_ready: {
    title: '等待确认形象',
    hint: '已生成1张形象图，可以继续或重试',
    step: 2,
    tone: 'action'
  },
  model_pending: {
    title: '等待白模制作',
    hint: '已选好形象，等待白模完成',
    step: 3,
    tone: 'processing'
  },
  model_ready: { title: '可以确认定制', hint: '白模预览已准备好', step: 4, tone: 'ready' },
  failed: { title: '需要重新处理', hint: '这次生成没有完成', step: 0, tone: 'failed' }
}
const FILTERS = [
  { key: 'all', text: '全部' },
  { key: 'ongoing', text: '制作中' },
  { key: 'action', text: '待确认' },
  { key: 'ready', text: '已就绪' }
]
function match(work, key) {
  if (key === 'ongoing') return work.status === 'image_pending' || work.status === 'model_pending'
  if (key === 'action') return work.status === 'image_ready' || work.status === 'failed'
  if (key === 'ready') return work.status === 'model_ready'
  return true
}
Page({
  data: {
    all: [],
    items: [],
    filters: FILTERS,
    filter: 'all',
    loading: true,
    deletingId: '',
    error: '',
    ongoing: 0
  },
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
    this.setData({ loading: this.data.all.length === 0, error: '' })
    try {
      const works = await request('GET', '/api/works')
      works.sort((a, b) => (b.created_at || '').localeCompare(a.created_at || ''))
      const all = works.map((w) => {
        const state = STATES[w.status] || {
          title: '处理中',
          hint: '点击查看详情',
          step: 1,
          tone: 'processing'
        }
        const preview = w.preview || [],
          candidates = w.candidates || []
        const cover = preview[0] || candidates[w.selected >= 0 ? w.selected : 0] || w.original
        const date = (w.created_at || '').replace('T', ' ').slice(0, 16)
        return { ...w, cover, date, state, shortid: (w.id || '').slice(0, 8) }
      })
      this.setData({
        all,
        items: all.filter((w) => match(w, this.data.filter)),
        loading: false,
        ongoing: all.filter((w) => match(w, 'ongoing')).length
      })
    } catch (e) {
      this.setData({ loading: false, error: e.message || '加载失败' })
      tip(e)
    }
  },
  chooseFilter(e) {
    const filter = e.currentTarget.dataset.filter
    this.setData({ filter, items: this.data.all.filter((w) => match(w, filter)) })
  },
  open(e) {
    const w = this.data.all.find((x) => x.id === e.currentTarget.dataset.id)
    if (!w) return
    const path =
      w.status === 'model_ready' ||
      w.status === 'model_pending' ||
      (w.status === 'failed' && w.selected >= 0)
        ? 'preview'
        : 'choose'
    wx.navigateTo({ url: '/pages/' + path + '/index?id=' + w.id })
  },
  confirmDelete(e) {
    const id = e.currentTarget.dataset.id
    if (!id || this.data.deletingId || !this.data.all.some((w) => w.id === id)) return
    wx.showModal({
      title: '删除作品',
      content: '删除后作品将从列表中移除，无法恢复。已有订单或正在执行生成任务的作品暂不能删除。',
      confirmText: '删除',
      confirmColor: '#BF6653',
      success: ({ confirm }) => {
        if (confirm) this.deleteWork(id)
      }
    })
  },
  async deleteWork(id) {
    if (this.data.deletingId) return
    this.setData({ deletingId: id })
    try {
      await request('DELETE', '/api/works/' + encodeURIComponent(id))
      const all = this.data.all.filter((work) => work.id !== id)
      this.setData({
        all,
        items: all.filter((work) => match(work, this.data.filter)),
        ongoing: all.filter((work) => match(work, 'ongoing')).length,
        deletingId: ''
      })
      wx.showToast({ title: '作品已删除', icon: 'success' })
    } catch (error) {
      this.setData({ deletingId: '' })
      tip(error)
    }
  },
  create() {
    wx.navigateTo({ url: '/pages/create/index' })
  },
  retry() {
    this.refresh()
  }
})
