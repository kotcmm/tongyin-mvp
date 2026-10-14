// 同一订单在列表页和详情页使用相同的状态、价格及日期格式。
const ORDER_LABELS = {
  awaiting_payment: '待付款',
  paid: '已付款',
  printing: '制作中',
  shipped: '已发货',
  completed: '已完成',
  canceled: '已取消'
}

function formatOrder(order) {
  return {
    ...order,
    label: ORDER_LABELS[order.status] || order.status,
    shortid: (order.id || '').slice(0, 8),
    price: (order.price_fen / 100).toFixed(2),
    date: (order.created_at || '').replace('T', ' ').slice(0, 16)
  }
}

module.exports = {ORDER_LABELS, formatOrder}
