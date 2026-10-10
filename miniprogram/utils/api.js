function tip(err){wx.showToast({title:err.message||'请求失败',icon:'none',duration:2600})}
async function request(method, path, data) {
  const app = getApp(), token = await app.ensureLogin()
  return new Promise((resolve, reject) => wx.request({
    url: app.globalData.apiBase + path, method, data,
    header: {'Authorization':'Bearer '+token, 'Content-Type':'application/json'},
    success(res) {
      if (res.statusCode >= 200 && res.statusCode < 300) resolve(res.data)
      else reject(new Error(res.data?.error || '接口错误 ' + res.statusCode))
    }, fail: reject
  }))
}
async function upload(path, localFile) {
 const app=getApp(), token=await app.ensureLogin()
 return new Promise((resolve,reject)=>wx.uploadFile({url:app.globalData.apiBase+path,filePath:localFile,name:'file',header:{'Authorization':'Bearer '+token},
   success(res){let data;try{data=JSON.parse(res.data)}catch(e){return reject(new Error('上传结果不合法'))}
     if(res.statusCode>=200&&res.statusCode<300)resolve(data);else reject(new Error(data.error||'上传失败'))},fail:reject}))
}
module.exports={request,upload,tip}
