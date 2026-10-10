const {request,tip}=require('../../utils/api')
Page({data:{id:'',item:null,paying:false},onLoad(q){this.setData({id:q.id});this.refresh()},onShow(){if(this.data.id)this.refresh()},
async refresh(){try{const x=await request('GET','/api/orders/'+this.data.id);const labels={awaiting_payment:'待付款',paid:'已付款，等待制作',printing:'正在打印',shipped:'已发货',completed:'已完成',canceled:'已取消'};this.setData({item:{...x,label:labels[x.status]||x.status,price:(x.price_fen/100).toFixed(2)}})}catch(e){tip(e)}},
async pay(){if(this.data.paying)return;this.setData({paying:true});try{
 const p=await request('POST','/api/orders/'+this.data.id+'/pay',{});
 await new Promise((resolve,reject)=>wx.requestPayment({...p,success:resolve,fail:reject}));
 wx.showToast({title:'支付已提交',icon:'success'});setTimeout(()=>this.refresh(),1200)
}catch(e){if(!String(e.errMsg||'').includes('cancel'))tip(e)}finally{this.setData({paying:false})}}
})
