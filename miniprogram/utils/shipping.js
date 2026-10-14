// 收货地址的输入校验及微信地址选择在结算页和订单详情页复用。
function normalizeShipping(name, phone, address) {
  const result = {
    name: String(name || '').trim(),
    phone: String(phone || '').trim(),
    address: String(address || '').trim()
  }
  if (result.name.length < 2 || result.phone.length < 6 || result.address.length < 8) {
    return null
  }
  return result
}

function chooseAddress(onSelected) {
  if (typeof wx.chooseAddress !== 'function') {
    wx.showToast({title: '请手动填写收货地址', icon: 'none'})
    return
  }
  wx.chooseAddress({
    success(result) {
      onSelected({
        name: result.userName || '',
        phone: result.telNumber || '',
        address: [
          result.provinceName || '',
          result.cityName || '',
          result.countyName || '',
          result.detailInfo || ''
        ].join('')
      })
    },
    fail(error) {
      // 主动取消不应显示报错。
      if (/cancel/i.test(error && error.errMsg || '')) return
      wx.showToast({title: '微信地址暂不可用，请手动填写', icon: 'none'})
    }
  })
}

module.exports = {normalizeShipping, chooseAddress}
