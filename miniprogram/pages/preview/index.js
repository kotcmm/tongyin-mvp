const {request,tip}=require('../../utils/api')
Page({
 data:{
  id:'',item:null,price:'--',name:'',phone:'',address:'',busy:false,
  dev:false,aiAutoEnabled:true,
  viewerUrl:'',modelKey:'',viewerLoading:false,viewerFailed:false,show3D:false,
  renderWidth:760,renderHeight:660,hasPreviews:false,viewMode:'model'
 },
 onLoad(q){
  const info=typeof wx.getWindowInfo==='function'?wx.getWindowInfo():wx.getSystemInfoSync()
  const ratio=Math.min(2,info.pixelRatio||2)
  this.setData({
   id:q.id,renderWidth:Math.max(300,Math.floor((info.windowWidth-62)*ratio)),
   renderHeight:Math.floor(290*ratio)
  })
  request('GET','/api/config').then(x=>this.setData({
   price:(x.price_fen/100).toFixed(2),dev:!!x.dev,
   aiAutoEnabled:x.ai_auto_enabled!==false
  })).catch(()=>{})
 },
 onShow(){
  this.refresh()
  this.poll=setInterval(()=>this.refresh(true),5000)
 },
 onHide(){this.cleanup();this.setData({show3D:false})},
 onUnload(){this.cleanup()},
 cleanup(){
  clearInterval(this.poll)
  clearTimeout(this.viewTimer)
 },
 beginModelLoad(){
  clearTimeout(this.viewTimer)
  this.viewTimer=setTimeout(()=>{
   if(this.data.viewerLoading)this.setData({viewerLoading:false,viewerFailed:true})
  },30000)
 },
 async refresh(silent){
  if(!this.data.id)return
  try{
   const item=await request('GET','/api/works/'+this.data.id)
   const modelKey=(item.model||'').split('?')[0]
   const hasPreviews=!!(item.preview&&item.preview.length)
   const isNew=modelKey && (modelKey!==this.data.modelKey||!this.data.show3D) && this.data.viewMode==='model'
   if(isNew){
    this.setData({
     item,modelKey,viewerUrl:item.model,
     viewerLoading:true,viewerFailed:false,show3D:true,hasPreviews
    })
    this.beginModelLoad()
   }else this.setData({item,hasPreviews})
   if(item.model || item.status==='failed')clearInterval(this.poll)
  }catch(e){if(!silent)tip(e)}
 },
 onModelLoaded(){
  clearTimeout(this.viewTimer)
  this.setData({viewerLoading:false,viewerFailed:false})
 },
 onModelError(){
  clearTimeout(this.viewTimer)
  this.setData({viewerLoading:false,viewerFailed:true})
 },
 retryModel(){
  clearTimeout(this.viewTimer)
  this.setData({show3D:false,viewerFailed:false,viewerLoading:true,viewMode:'model'})
  wx.nextTick(()=>{
   this.setData({show3D:true})
   this.beginModelLoad()
  })
 },
 showImages(){
  this.cleanup()
  this.setData({viewMode:'images',show3D:false,viewerLoading:false})
 },
 showModel(){
  if(!this.data.item||!this.data.item.model)return
  this.setData({viewMode:'model',show3D:true,viewerLoading:true,viewerFailed:false})
  this.beginModelLoad()
 },
 name(e){this.setData({name:e.detail.value})},
 phone(e){this.setData({phone:e.detail.value})},
 address(e){this.setData({address:e.detail.value})},
 toWorks(){wx.switchTab({url:'/pages/works/index'})},
 async submit(){
  if(this.data.busy)return
  this.setData({busy:true})
  try{
   const o=await request('POST','/api/orders',{
    work_id:this.data.id,name:this.data.name,phone:this.data.phone,address:this.data.address
   })
   wx.redirectTo({url:'/pages/order/index?id='+o.id})
  }catch(e){tip(e)}
  finally{this.setData({busy:false})}
 }
})
