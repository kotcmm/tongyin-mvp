const {request,tip}=require('../../utils/api')
Page({
 data:{items:[],loading:true,error:''},
 onShow(){this.refresh()},
 onPullDownRefresh(){this.refresh().then(()=>wx.stopPullDownRefresh(),()=>wx.stopPullDownRefresh())},
 async refresh(){
  try{
   const items=await request('GET','/api/orders')
   const labels={awaiting_payment:'待付款',paid:'已付款',printing:'制作中',shipped:'已发货',completed:'已完成',canceled:'已取消'}
   items.sort((a,b)=>(b.created_at||'').localeCompare(a.created_at||''))
   this.setData({items:items.map(x=>({...x,label:labels[x.status]||x.status,shortid:x.id.slice(0,8),price:(x.price_fen/100).toFixed(2),date:(x.created_at||'').replace('T',' ').slice(0,16)})),loading:false,error:''})
  }catch(e){this.setData({loading:false,error:e.message||'加载失败'});tip(e)}
 },
 open(e){wx.navigateTo({url:'/pages/order/index?id='+e.currentTarget.dataset.id})},
 make(){wx.switchTab({url:'/pages/home/index'})},
 toWorks(){wx.switchTab({url:'/pages/works/index'})}
})