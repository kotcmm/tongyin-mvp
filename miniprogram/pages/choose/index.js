const {request,tip}=require('../../utils/api')
Page({
 data:{id:'',item:null,selected:-1,busy:false,dev:false,aiAutoEnabled:true,loading:true},
 onLoad(q){
  this.setData({id:q.id})
  request('GET','/api/config').then(cfg=>this.setData({dev:!!cfg.dev,aiAutoEnabled:cfg.ai_auto_enabled!==false})).catch(()=>{})
 },
 onShow(){this.refresh();this.poll=setInterval(()=>this.refresh(true),5000)},
 onHide(){clearInterval(this.poll)},
 onUnload(){clearInterval(this.poll)},
 async refresh(silent){
  if(!this.data.id)return
  try{
   const item=await request('GET','/api/works/'+this.data.id)
   this.setData({item,loading:false})
   if(item.status==='model_pending'||item.status==='model_ready'){
    clearInterval(this.poll)
    wx.redirectTo({url:'/pages/preview/index?id='+this.data.id})
   }else if(item.status==='image_ready')clearInterval(this.poll)
  }catch(e){this.setData({loading:false});if(!silent)tip(e)}
 },
 select(e){this.setData({selected:Number(e.currentTarget.dataset.index)})},
 async confirm(){
  if(this.data.selected<0||this.data.busy)return
  this.setData({busy:true})
  try{await request('POST','/api/works/'+this.data.id+'/select',{index:this.data.selected});wx.redirectTo({url:'/pages/preview/index?id='+this.data.id})}
  catch(e){tip(e)}
  finally{this.setData({busy:false})}
 },
 toWorks(){wx.switchTab({url:'/pages/works/index'})}
})