const {request,tip}=require('../../utils/api')
Page({
 data:{id:'',item:null,price:'--',name:'',phone:'',address:'',busy:false,dev:false,aiAutoEnabled:true,hasPreviews:false},
 onLoad(q){
  this.setData({id:q.id})
  request('GET','/api/config').then(x=>this.setData({
   price:(x.price_fen/100).toFixed(2),dev:!!x.dev,aiAutoEnabled:x.ai_auto_enabled!==false
  })).catch(()=>{})
 },
 onShow(){this.refresh();this.timer=setInterval(()=>this.refresh(true),5000)},
 onHide(){clearInterval(this.timer)},
 onUnload(){clearInterval(this.timer)},
 async refresh(silent){
  if(!this.data.id)return
  try{
   const item=await request('GET','/api/works/'+this.data.id)
   this.setData({item,hasPreviews:!!(item.preview&&item.preview.length)})
   if(item.model||item.status==='failed')clearInterval(this.timer)
  }catch(e){if(!silent)tip(e)}
 },
 open3D(){
  const item=this.data.item
  if(!item||!item.model)return
  wx.navigateTo({url:'/pages/viewer/index?model='+encodeURIComponent(item.model)})
 },
 name(e){this.setData({name:e.detail.value})},
 phone(e){this.setData({phone:e.detail.value})},
 address(e){this.setData({address:e.detail.value})},
 toWorks(){wx.switchTab({url:'/pages/works/index'})},
 async submit(){
  if(this.data.busy)return
  this.setData({busy:true})
  try{
   const order=await request('POST','/api/orders',{
    work_id:this.data.id,name:this.data.name,phone:this.data.phone,address:this.data.address
   })
   wx.redirectTo({url:'/pages/order/index?id='+order.id})
  }catch(e){tip(e)}finally{this.setData({busy:false})}
 }
})
