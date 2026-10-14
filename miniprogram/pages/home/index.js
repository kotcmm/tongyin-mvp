const {request}=require('../../utils/api')
const {loadIdentity}=require('../../utils/identity')
Page({
  data:{recent:null,count:0,identityState:'loading',identityLabel:'确认账号中…'},
  onShow(){this.refresh();this.refreshIdentity()},
  onHide(){this._identityRequest=(this._identityRequest||0)+1},
  async refreshIdentity(){
    const seq=this._identityRequest=(this._identityRequest||0)+1
    this.setData({identityState:'loading',identityLabel:'确认账号中…'})
    try{
      const identity=await loadIdentity()
      if(seq!==this._identityRequest)return
      this.setData({identityState:identity.mode,identityLabel:identity.label})
    }catch(e){
      if(seq!==this._identityRequest)return
      this.setData({identityState:'error',identityLabel:'账号连接失败'})
    }
  },
  accountAction(){
    if(this.data.identityState==='error'){this.refreshIdentity();return}
    if(this.data.identityState==='loading')return
    this.toWorks()
  },
  async refresh(){
    try{
      const items=await request('GET','/api/works')
      items.sort((a,b)=>(b.created_at||'').localeCompare(a.created_at||''))
      const recent=items[0]||null
      const labels={image_pending:'等待形象生成',image_ready:'可以挑选形象',model_pending:'正在制作白模',model_ready:'白模已准备好',failed:'需要重新处理'}
      this.setData({recent:recent?{...recent,cover:(recent.preview||[])[0]||(recent.candidates||[])[0]||recent.original,label:labels[recent.status]||'查看进度'}:null,count:items.length})
    }catch(e){/* 首页不因作品接口失败而阻止新建 */ }
  },
  toCreate(){wx.navigateTo({url:'/pages/create/index'})},
  toWorks(){wx.switchTab({url:'/pages/works/index'})},
  toOrders(){wx.switchTab({url:'/pages/orders/index'})},
  openRecent(){
    const w=this.data.recent
    if(!w)return
    const page=w.status==='model_pending'||w.status==='model_ready'||(w.status==='failed'&&w.selected>=0)?'preview':'choose'
    wx.navigateTo({url:'/pages/'+page+'/index?id='+w.id})
  }
})
