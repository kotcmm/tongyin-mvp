const {API_BASE}=require('../../config')
Page({
 data:{viewerUrl:'',error:''},
 onLoad(q){
  let model=q.model||''
  if(model.indexOf('http%3A')===0||model.indexOf('https%3A')===0){
   try{model=decodeURIComponent(model)}catch(e){}
  }
  // Reject untrusted URLs and only pass back the signed GLB URL
  // that was returned for the current work from our own backend.
  const prefix=API_BASE+'/media/'
  if(!model.startsWith(prefix)||!model.includes('signature=')){
   this.setData({error:'模型地址无效，请返回作品页面重新打开'})
   return
  }
  const url=API_BASE+'/admin/3d/?model='+encodeURIComponent(model)
  this.setData({viewerUrl:url})
 }
})
