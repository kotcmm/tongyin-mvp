const {request,tip}=require('../../utils/api')
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
   const labels={awaiting_payment:'等待付款',paid:'已付款，等待制作',printing:'正在打印',shipped:'已发货',completed:'已完成',canceled:'已取消'}
   const item={...x,label:labels[x.status]||x.status,price:(x.price_fen/100).toFixed(2),date:(x.created_at||'').replace('T',' ').slice(0,16)}
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
  if(typeof wx.chooseAddress!=='function'){
   wx.showToast({title:'当前无法选择微信地址，请手动填写',icon:'none'})
   return
  }
  wx.chooseAddress({
   success:res=>{
    this.setData({
     editName:res.userName||'',editPhone:res.telNumber||'',
     editAddress:[res.provinceName||'',res.cityName||'',res.countyName||'',res.detailInfo||''].join('')
    })
   },
   fail:e=>{
    if(/cancel/i.test((e&&e.errMsg)||''))return
    wx.showToast({title:'微信地址暂不可用，请手动填写',icon:'none'})
   }
  })
 },
 async saveShipping(){
  if(this.data.savingShipping||!this.data.item||this.data.item.status!=='awaiting_payment')return
  const name=(this.data.editName||'').trim()
  const phone=(this.data.editPhone||'').trim()
  const address=(this.data.editAddress||'').trim()
  if(name.length<2||phone.length<6||address.length<8){
   wx.showToast({title:'请填写完整收货信息',icon:'none'})
   return
  }
  this.setData({savingShipping:true})
  try{
   const updated=await request('PUT','/api/orders/'+this.data.id+'/shipping-address',{name,phone,address})
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
