// 发布前改为已备案并配置到小程序 request/uploadFile 合法域名的 HTTPS API 地址。
module.exports = {
  API_BASE: 'http://localhost:18081' // .NET migration test branch only; production must use HTTPS,
  DEV_LOGIN: true // 仅后端 DEV_MODE=true 时，供开发者工具本地联调用
}
