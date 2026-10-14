const {request,tip}=require('../../utils/api')
const {loadIdentity}=require('../../utils/identity')
const STATES={
  image_pending:{title:'等待形象制作',hint:'作品已收到，等待处理',step:1,tone:'processing'},
  image_ready:{title:'等待确认形象',hint:'已生成1张形象图，可以继续或重试',step:2,tone:'action'},
  model_pending:{title:'等待白模制作',hint:'已选好形象，等待白模完成',step:3,tone:'processing'},
  model_ready:{title:'可以确认定制',hint:'白模预览已准备好',step:4,tone:'ready'},
  failed:{title:'需要重新处理',hint:'这次生成没有完成',step:0,tone:'failed'}
}
const FILTERS=[{key:'all',text:'全部'},{key:'ongoing',text:'制作中'},{key:'action',text:'待确认'},{key:'ready',text:'已就绪'}]
function match(work,key){
  if(key==='ongoing')return work.status==='image_pending'||work.status==='model_pending'
  if(key==='action')return work.status==='image_ready'||work.status==='failed'
  if(key==='ready')return work.status==='model_ready'
  return true
}
Page({
  data:{all:[],items:[],filters:FILTERS,filter:'all',loading:true,error:'',ongoing:0,identityState:'loading',identityLabel:'正在确认账号',identityCode:'',identityHint:''},
  onShow(){this.refreshIdentity();this.refresh()},
  onHide(){this._identityRequest=(this._identityRequest||0)+1},
  async refreshIdentity(){
    const seq=this._identityRequest=(this._identityRequest||0)+1
    this.setData({identityState:'loading',identityLabel:'正在确认账号',identityHint:''})
    try{
      const identity=await loadIdentity()
      if(seq!==this._identityRequest)return
      this.setData({identityState:identity.mode,identityLabel:identity.label,
        identityCode:identity.accountCode,identityHint:identity.description})
    }catch(e){
      if(seq!==this._identityRequest)return
      this.setData({identityState:'error',identityLabel:'账号状态获取失败',
        identityCode:'',identityHint:'请检查网络连接，点击重新确认'})
    }
  },
  onPullDownRefresh(){this.refresh().then(()=>wx.stopPullDownRefresh(),()=>wx.stopPullDownRefresh())},
  async refresh(){
    this.setData({loading:this.data.all.length===0,error:''})
    try{
      const works=await request('GET','/api/works')
      works.sort((a,b)=>(b.created_at||'').localeCompare(a.created_at||''))
      const all=works.map(w=>{
        const state=STATES[w.status]||{title:'处理中',hint:'点击查看详情',step:1,tone:'processing'}
        const preview=w.preview||[], candidates=w.candidates||[]
        const cover=preview[0]||candidates[w.selected>=0?w.selected:0]||w.original
        const date=(w.created_at||'').replace('T',' ').slice(0,16)
        return {...w,cover,date,state,shortid:(w.id||'').slice(0,8)}
      })
      this.setData({all,items:all.filter(w=>match(w,this.data.filter)),loading:false,ongoing:all.filter(w=>match(w,'ongoing')).length})
    }catch(e){this.setData({loading:false,error:e.message||'加载失败'});tip(e)}
  },
  chooseFilter(e){
    const filter=e.currentTarget.dataset.filter
    this.setData({filter,items:this.data.all.filter(w=>match(w,filter))})
  },
  open(e){
    const w=this.data.all.find(x=>x.id===e.currentTarget.dataset.id)
    if(!w)return
    const path=w.status==='model_ready'||w.status==='model_pending'||(w.status==='failed'&&w.selected>=0)?'preview':'choose'
    wx.navigateTo({url:'/pages/'+path+'/index?id='+w.id})
  },
  create(){wx.navigateTo({url:'/pages/create/index'})},
  retry(){this.refresh()}
})
