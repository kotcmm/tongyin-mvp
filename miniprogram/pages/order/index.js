const {request,tip}=require('../../utils/api')
const {normalizeShipping,chooseAddress}=require('../../utils/shipping')
const {formatOrder}=require('../../utils/order')
Page({
 data:{
  id:'',item:null,paying:false,editingShipping:false,savingShipping:false,
  editName:'',editPhone:'',editAddress:'',
  tracking:null,trackingLoading:false,trackingError:''
 },
 onLoad(q){
  this._editOnOpen=q.editAddress==='1'
  this.setData({id:q.id})
 },
 onShow(){if(this.data.id)this.refresh()},
 async refresh(){
  try{
   const x=await request('GET','/api/orders/'+this.data.id)
   const item=formatOrder(x)
   const openEdit=this._editOnOpen&&item.status==='awaiting_payment'
   this._editOnOpen=false
   this.setData({
    item,editName:item.name,editPhone:item.phone,editAddress:item.address,
    editingShipping:openEdit
   })
   if(item.status==='shipped'||item.status==='completed')this.loadTracking()
   else this.setData({tracking:null,trackingError:''})
  }catch(e){tip(e)}
 },
 async loadTracking(){
  this.setData({trackingLoading:true,trackingError:''})
  try{
   const v=await request('GET','/api/orders/'+this.data.id+'/tracking')
   this.setData({tracking:v})
  }catch(e){
   this.setData({trackingError:e.message||'物流暂时无法查询'})
  }finally{this.setData({trackingLoading:false})}
 },
 copyTracking(){
  const number=this.data.tracking&&this.data.tracking.tracking_no||this.data.item&&this.data.item.tracking
  if(!number)return
  wx.setClipboardData({data:number})
 },
 startShippingEdit(){
  if(!this.data.item || this.data.item.status!=='awaiting_payment')return
  this.setData({editingShipping:true,editName:this.data.item.name,editPhone:this.data.item.phone,editAddress:this.data.item.address})
 },
 cancelShippingEdit(){
  this.setData({editingShipping:false,editName:this.data.item.name,editPhone:this.data.item.phone,editAddress:this.data.item.address})
 },
 changeName(e){this.setData({editName:e.detail.value})},
 changePhone(e){this.setData({editPhone:e.detail.value})},
 changeAddress(e){this.setData({editAddress:e.detail.value})},
 chooseWeChatAddress(){
  chooseAddress(details=>{
   this.setData({editName:details.name,editPhone:details.phone,editAddress:details.address})
  })
 },

 async saveShipping(){
  if(this.data.savingShipping||!this.data.item||this.data.item.status!=='awaiting_payment')return
  const details=normalizeShipping(this.data.editName,this.data.editPhone,this.data.editAddress)
  if(!details){
   wx.showToast({title:'请填写完整收货信息',icon:'none'})
   return
  }
  this.setData({savingShipping:true})
  try{
   const updated=await request('PUT','/api/orders/'+this.data.id+'/shipping-address',details)
   this.setData({
    item:{...this.data.item,name:updated.name,phone:updated.phone,address:updated.address,status:updated.status},
    editName:updated.name,editPhone:updated.phone,editAddress:updated.address,
    editingShipping:false
   })
   wx.showToast({title:'收货地址已更新',icon:'success'})
  }catch(e){
   tip(e)
   if(e.statusCode===409||e.statusCode===404)this.refresh()
  }finally{this.setData({savingShipping:false})}
 },
 toWorks(){wx.switchTab({url:'/pages/works/index'})},
 async cancelOrder(){
  const item=this.data.item
  if(!item || item.status!=='awaiting_payment' || item.payment_started_at)return
  const yes=await new Promise(resolve=>wx.showModal({
   title:'取消这笔订单？',
   content:'未付款订单取消后仍会保留原作品，可以重新创建订单。',
   success:res=>resolve(!!res.confirm),fail:()=>resolve(false)
  }))
  if(!yes)return
  try{
   await request('POST','/api/orders/'+this.data.id+'/cancel',{reason:'用户主动取消未付款订单'})
   wx.showToast({title:'订单已取消',icon:'success'})
   this.refresh()
  }catch(e){tip(e)}
 },
 async pay(){
  if(this.data.paying || this.data.editingShipping || this.data.savingShipping)return
  this.setData({paying:true})
  try{
   const p=await request('POST','/api/orders/'+this.data.id+'/pay',{})
   await new Promise((resolve,reject)=>wx.requestPayment({...p,success:resolve,fail:reject}))
   wx.showToast({title:'支付已提交',icon:'success'})
   setTimeout(()=>this.refresh(),1200)
  }catch(e){if(!String(e.errMsg||'').includes('cancel'))tip(e)}
  finally{this.setData({paying:false})}
 }
})
