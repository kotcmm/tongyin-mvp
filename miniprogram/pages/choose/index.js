const {request,tip}=require('../../utils/api')
Page({
 data:{id:'',item:null,busy:false,dev:false,aiAutoEnabled:true,loading:true,maxAttempts:2},
 onLoad(q){
  this.setData({id:q.id})
  request('GET','/api/config').then(cfg=>this.setData({
    dev:!!cfg.dev,aiAutoEnabled:cfg.ai_auto_enabled!==false,maxAttempts:cfg.free_image_attempts||2
  })).catch(()=>{})
 },
 onShow(){this.refresh();this.beginPolling()},
 onHide(){this.stopPolling()},
 onUnload(){this.stopPolling()},
 beginPolling(){this.stopPolling();this.poll=setInterval(()=>this.refresh(true),5000)},
 stopPolling(){if(this.poll){clearInterval(this.poll);this.poll=null}},
 async refresh(silent){
  if(!this.data.id)return
  try{
   const item=await request('GET','/api/works/'+this.data.id)
   const used=item.image_attempts||0
   this.setData({item,loading:false,canRetry:used<this.data.maxAttempts,remaining:Math.max(0,this.data.maxAttempts-used)})
   if(item.status==='model_pending'||item.status==='model_ready'){
    this.stopPolling()
    wx.redirectTo({url:'/pages/preview/index?id='+this.data.id})
   }else if(item.status==='image_ready')this.stopPolling()
  }catch(e){this.setData({loading:false});if(!silent)tip(e)}
 },
 async confirm(){
  if(this.data.busy||!this.data.item||this.data.item.status!=='image_ready')return
  this.setData({busy:true})
  try{
   await request('POST','/api/works/'+this.data.id+'/select',{index:0})
   wx.redirectTo({url:'/pages/preview/index?id='+this.data.id})
  }catch(e){tip(e)}finally{this.setData({busy:false})}
 },
 retry(){
  if(this.data.busy||!this.data.canRetry)return
  wx.showModal({
   title:'重新生成形象？',
   content:'本次只会生成1张新图，将替换当前效果。当前还剩'+this.data.remaining+'次免费机会，确认继续吗？',
   confirmText:'重新生成',
   success:res=>{if(res.confirm)this.submitRetry()}
  })
 },
 async submitRetry(){
  if(this.data.busy)return
  this.setData({busy:true})
  try{
   await request('POST','/api/works/'+this.data.id+'/generate',{})
   await this.refresh()
   this.beginPolling()
  }catch(e){tip(e)}finally{this.setData({busy:false})}
 },
 toWorks(){wx.switchTab({url:'/pages/works/index'})}
})
