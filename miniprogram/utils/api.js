function tip(err) {
  wx.showToast({ title: err.message || '请求失败', icon: 'none', duration: 2600 })
}
function failWithStatus(res, prefix) {
  const message = (res.data && res.data.error) || prefix + ' ' + res.statusCode
  const error = new Error(message)
  error.statusCode = res.statusCode
  return error
}
async function withAuth(call) {
  const app = getApp()
  let token = await app.ensureLogin()
  try {
    return await call(token)
  } catch (e) {
    if (e.statusCode !== 401) throw e
    app.clearLogin()
    token = await app.ensureLogin()
    return call(token)
  }
}
async function request(method, path, data) {
  return withAuth(
    (token) =>
      new Promise((resolve, reject) =>
        wx.request({
          url: getApp().globalData.apiBase + path,
          method,
          data,
          header: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
          success(res) {
            if (res.statusCode >= 200 && res.statusCode < 300) resolve(res.data)
            else reject(failWithStatus(res, '接口错误'))
          },
          fail: reject
        })
      )
  )
}
async function upload(path, localFile) {
  return withAuth(
    (token) =>
      new Promise((resolve, reject) =>
        wx.uploadFile({
          url: getApp().globalData.apiBase + path,
          filePath: localFile,
          name: 'file',
          header: { Authorization: 'Bearer ' + token },
          success(res) {
            let data
            try {
              data = JSON.parse(res.data)
            } catch (e) {
              return reject(new Error('上传结果不合法'))
            }
            if (res.statusCode >= 200 && res.statusCode < 300) return resolve(data)
            const error = new Error(data.error || '上传失败')
            error.statusCode = res.statusCode
            reject(error)
          },
          fail: reject
        })
      )
  )
}
module.exports = { request, upload, tip }
