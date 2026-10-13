const {request,tip}=require('../../utils/api')
const {createScopedThreejs}=require('../../vendor/threejs-miniprogram/index')
const {registerGLTFLoader}=require('../../vendor/threejs-miniprogram/gltf-loader')

// Native mini-program WebGL canvas, not XR Frame and not a web-view.
Page({
 data:{
  id:'',item:null,price:'--',name:'',phone:'',address:'',busy:false,
  dev:false,aiAutoEnabled:true,
  showCanvas:false,viewerLoading:false,viewerError:'',viewerMessage:'正在加载白模…',fullscreen:false,
  shippingLoaded:false,shippingEditing:false,shippingExists:false,shippingSaving:false,
  existingOrder:null,checkingOrder:true,orderLookupFailed:false
 },
 onLoad(q){
  this.setData({id:q.id})
  this.loadShipping()
  request('GET','/api/config').then(x=>this.setData({
   price:(x.price_fen/100).toFixed(2),dev:!!x.dev,aiAutoEnabled:x.ai_auto_enabled!==false
  })).catch(()=>{})
 },
 onReady(){
  this._pageReady=true
  this.prepareViewer()
 },
 onShow(){
  this._visible=true
  this.loadExistingOrder()
  this.refresh()
  this.timer=setInterval(()=>this.refresh(true),5000)
 },
 onHide(){
  this._visible=false
  clearInterval(this.timer)
 },
 onUnload(){
  this._visible=false
  clearInterval(this.timer)
  this.disposeViewer()
 },
 async refresh(silent){
  if(!this.data.id)return
  try{
   const item=await request('GET','/api/works/'+this.data.id)
   const key=item.model ? item.model.split('?')[0] : ''
   this.setData({item},()=>{
    if(key && key!==this._loadedKey)this.prepareViewer()
   })
   if(item.model||item.status==='failed')clearInterval(this.timer)
  }catch(e){if(!silent)tip(e)}
 },
 prepareViewer(){
  const item=this.data.item
  if(!this._pageReady||!this._visible||!item||!item.model)return
  const key=item.model.split('?')[0]
  if(this._loadedKey===key && (this._renderer||this._initializing))return
  this.disposeViewer()
  this._loadedKey=key
  this._initializing=true
  this.setData({showCanvas:true,viewerLoading:true,viewerError:'',viewerMessage:'正在准备 3D 画布…'},()=>{
   wx.nextTick(()=>this.initializeWebGL(item.model,key))
  })
 },
 initializeWebGL(url,key){
  wx.createSelectorQuery().select('#modelCanvas').fields({node:true,size:true}).exec(result=>{
   if(this._loadedKey!==key||!this._initializing)return
   const entry=result&&result[0]
   if(!entry||!entry.node||!entry.width||!entry.height){
    this.showViewerError('无法创建 3D 画布。请确认微信开发者工具已开启 WebGL 渲染。')
    return
   }
   try{
    const canvas=entry.node
    const THREE=createScopedThreejs(canvas)
    THREE.__tongyinWhitePLA=true
    registerGLTFLoader(THREE)
    const system=wx.getSystemInfoSync()
    const pixelRatio=Math.min(2,system.pixelRatio||1)
    canvas.width=Math.max(1,Math.round(entry.width*pixelRatio))
    canvas.height=Math.max(1,Math.round(entry.height*pixelRatio))
    const renderer=new THREE.WebGLRenderer({canvas,antialias:true,alpha:false})
    renderer.setPixelRatio(pixelRatio)
    renderer.setSize(entry.width,entry.height,false)
    renderer.setClearColor(0xEDF1EB,1)
    renderer.gammaOutput=true
    renderer.gammaFactor=2.2
    const scene=new THREE.Scene()
    scene.background=new THREE.Color(0xEDF1EB)
    const aspect=entry.width/entry.height
    const camera=new THREE.OrthographicCamera(-aspect,aspect,1,-1,.01,100)
    camera.position.set(0,.35,3)
    camera.lookAt(0,0,0)
    const ambient=new THREE.HemisphereLight(0xffffff,0x98a99e,2.1)
    scene.add(ambient)
    const keyLight=new THREE.DirectionalLight(0xffffff,2.1)
    keyLight.position.set(3,5,6);scene.add(keyLight)
    const fill=new THREE.DirectionalLight(0xffffff,.85)
    fill.position.set(-4,2,-3);scene.add(fill)
    this._canvas=canvas
    this._THREE=THREE
    this._renderer=renderer
    this._scene=scene
    this._camera=camera
    this.setData({viewerMessage:'正在读取 GLB 模型…'})
    // Loader comes from the official WeChat Three.js MiniProgram example.
    // It uses the mini-program networking adapter; no browser DOM is required.
    new THREE.GLTFLoader().load(url,gltf=>{
     if(this._loadedKey!==key||!this._renderer)return
     try{
      const model=gltf.scene
      model.updateMatrixWorld(true)
      const bounds=new THREE.Box3().setFromObject(model)
      const size=bounds.getSize(new THREE.Vector3())
      const center=bounds.getCenter(new THREE.Vector3())
      const max=Math.max(size.x,size.y,size.z)
      if(!Number.isFinite(max)||max<.000001)throw Error('模型不含可显示的几何体')
      let count=0
      const white=new THREE.MeshStandardMaterial({
       color:0xf5f6f2,metalness:0,roughness:.86,side:THREE.DoubleSide
      })
      model.traverse(node=>{
       if(node.isMesh){
        node.material=white
        count++
       }
      })
      if(!count)throw Error('模型没有网格')
      const positioned=new THREE.Group()
      positioned.add(model)
      positioned.scale.setScalar(1/max)
      positioned.position.copy(center).multiplyScalar(-1/max)
      const orbit=new THREE.Group()
      orbit.add(positioned)
      scene.add(orbit)
      this._model=orbit
      this._initializing=false
      this.setData({viewerLoading:false,viewerError:''})
      this.paintViewer()
     }catch(err){this.showViewerError('GLB 解析失败：'+(err.message||err))}
    },undefined,err=>{
     if(this._loadedKey===key)this.showViewerError('GLB 加载失败：'+(err&&err.message||err||'网络或格式不兼容'))
    })
   }catch(err){this.showViewerError('3D 画布初始化失败：'+(err.message||err))}
  })
 },
 // Expand the existing canvas in place; preserve GLB, orbit and zoom state.
 // Re-measure it instead of re-creating or re-downloading the model.
 toggleFullscreen(){
  const expanded=!this.data.fullscreen
  this.setData({fullscreen:expanded},()=>{
   wx.nextTick(()=>this.resizeViewer())
  })
 },
 resizeViewer(){
  if(!this._renderer||!this._camera)return
  wx.createSelectorQuery().select('#modelCanvas').boundingClientRect(rect=>{
   if(!rect||!rect.width||!rect.height||!this._renderer)return
   this._renderer.setSize(rect.width,rect.height,false)
   const aspect=rect.width/rect.height
   this._camera.left=-aspect
   this._camera.right=aspect
   this._camera.top=1
   this._camera.bottom=-1
   this._camera.updateProjectionMatrix()
   this.paintViewer()
  }).exec()
 },
 zoomViewer(factor){
  if(!this._camera)return
  this._camera.zoom=Math.max(.6,Math.min(8,this._camera.zoom*factor))
  this._camera.updateProjectionMatrix()
  this.paintViewer()
 },
 zoomIn(){this.zoomViewer(1.4)},
 zoomOut(){this.zoomViewer(1/1.4)},
 resetView(){
  if(this._model){
   this._model.rotation.x=0
   this._model.rotation.y=0
  }
  if(this._camera){
   this._camera.zoom=1
   this._camera.updateProjectionMatrix()
  }
  this.paintViewer()
 },
 paintViewer(){
  if(!this._canvas||!this._renderer||!this._scene||!this._camera||this._frame)return
  const canvas=this._canvas
  this._frame=canvas.requestAnimationFrame(()=>{
   this._frame=0
   if(this._renderer)this._renderer.render(this._scene,this._camera)
  })
 },
 canvasTouchStart(e){
  const ts=e.touches||[]
  if(!ts.length)return
  this._lastTouch={x:ts[0].clientX,y:ts[0].clientY}
  this._pinch=ts.length>1?this.touchDistance(ts):0
 },
 touchDistance(ts){
  return Math.hypot(ts[0].clientX-ts[1].clientX,ts[0].clientY-ts[1].clientY)
 },
 canvasTouchMove(e){
  if(!this._model||!this._camera)return
  const ts=e.touches||[]
  if(ts.length>=2){
   const dist=this.touchDistance(ts)
   if(this._pinch>0){
    this._camera.zoom=Math.max(.6,Math.min(8,this._camera.zoom*dist/this._pinch))
    this._camera.updateProjectionMatrix()
   }
   this._pinch=dist
   this._lastTouch=null
  }else if(ts.length===1){
   const now={x:ts[0].clientX,y:ts[0].clientY}
   if(this._lastTouch){
    this._model.rotation.y+=(now.x-this._lastTouch.x)*.012
    this._model.rotation.x=Math.max(-.65,Math.min(.65,this._model.rotation.x+(now.y-this._lastTouch.y)*.007))
   }
   this._lastTouch=now
   this._pinch=0
  }
  this.paintViewer()
 },
 canvasTouchEnd(e){
  const ts=e.touches||[]
  this._lastTouch=ts.length===1?{x:ts[0].clientX,y:ts[0].clientY}:null
  this._pinch=ts.length>1?this.touchDistance(ts):0
 },
 showViewerError(message){
  this._initializing=false
  this.setData({viewerLoading:false,viewerError:message})
 },
 retryViewer(){
  this.disposeViewer()
  this.setData({showCanvas:false,viewerLoading:true,viewerError:''},()=>{
   wx.nextTick(()=>this.prepareViewer())
  })
 },
 disposeViewer(){
  if(this._canvas&&this._frame){
   this._canvas.cancelAnimationFrame(this._frame)
  }
  this._frame=0
  if(this._scene){
   this._scene.traverse(obj=>{
    if(obj.geometry&&obj.geometry.dispose)obj.geometry.dispose()
    if(obj.material){
     const materials=Array.isArray(obj.material)?obj.material:[obj.material]
     materials.forEach(mat=>{if(mat&&mat.dispose)mat.dispose()})
    }
   })
  }
  if(this._renderer){
   this._renderer.dispose()
  }
  if(this._THREE&&this._THREE.global&&this._THREE.global.unregisterCanvas&&this._canvas){
   this._THREE.global.unregisterCanvas(this._canvas)
  }
  this._initializing=false
  this._canvas=null;this._THREE=null;this._renderer=null
  this._scene=null;this._camera=null;this._model=null;this._loadedKey=''
  this._lastTouch=null;this._pinch=0
 },
 async loadExistingOrder(){
  if(!this.data.id)return
  this.setData({checkingOrder:true,orderLookupFailed:false})
  try{
   const response=await request('GET','/api/works/'+this.data.id+'/order')
   const order=response&&response.order||null
   const patch={existingOrder:order,checkingOrder:false,orderLookupFailed:false}
   if(order){
    Object.assign(patch,{name:order.name,phone:order.phone,address:order.address,
     shippingExists:true,shippingEditing:false,shippingLoaded:true})
   }
   this.setData(patch)
   return order
  }catch(e){
   this.setData({checkingOrder:false,orderLookupFailed:true})
   tip(e)
   return null
  }
 },
 openExistingOrder(editAddress){
  const order=this.data.existingOrder
  if(!order)return
  wx.navigateTo({url:'/pages/order/index?id='+order.id+(editAddress&&order.status==='awaiting_payment'?'&editAddress=1':'')})
 },
 changeShipping(){
  if(this.data.existingOrder){
   this.openExistingOrder(true)
   return
  }
  this.editShipping()
 },
 async loadShipping(){
  try{
   const response=await request('GET','/api/shipping-address')
   const address=response&&response.address
   if(this.data.existingOrder)return
   if(address&&address.name&&address.phone&&address.address){
    this.setData({
     name:address.name,phone:address.phone,address:address.address,
     shippingExists:true,shippingEditing:false,shippingLoaded:true
    })
   }else this.setData({shippingLoaded:true,shippingEditing:true,shippingExists:false})
  }catch(e){
   this.setData({shippingLoaded:true,shippingEditing:true})
   tip(e)
  }
 },
 editShipping(){this.setData({shippingEditing:true})},
 async saveShipping(){
  if(this.data.shippingSaving)return false
  const name=(this.data.name||'').trim()
  const phone=(this.data.phone||'').trim()
  const address=(this.data.address||'').trim()
  if(name.length<2 || phone.length<6 || address.length<8){
   wx.showToast({title:'请填写完整收货信息',icon:'none'})
   this.setData({shippingEditing:true})
   return false
  }
  if(this.data.existingOrder){this.openExistingOrder(true);return false}
  this.setData({shippingSaving:true})
  try{
   await request('PUT','/api/shipping-address',{name,phone,address})
   this.setData({name,phone,address,shippingExists:true,shippingEditing:false})
   return true
  }catch(e){
   tip(e)
   this.setData({shippingEditing:true})
   return false
  }finally{
   this.setData({shippingSaving:false})
  }
 },
 chooseWeChatAddress(){
  if(this.data.existingOrder){this.openExistingOrder(true);return}
  if(typeof wx.chooseAddress!=='function'){
   wx.showToast({title:'当前环境不支持微信地址，请手动填写',icon:'none'})
   this.setData({shippingEditing:true})
   return
  }
  wx.chooseAddress({
   success:res=>{
    const address=[
     res.provinceName||'',res.cityName||'',res.countyName||'',res.detailInfo||''
    ].join('')
    this.setData({name:res.userName||'',phone:res.telNumber||'',address,shippingEditing:true},()=>{
     this.saveShipping()
    })
   },
   fail:err=>{
    if(/cancel/i.test((err&&err.errMsg)||''))return
    this.setData({shippingEditing:true})
    wx.showToast({title:'微信地址暂不可用，请手动填写',icon:'none'})
   }
  })
 },
 name(e){this.setData({name:e.detail.value})},
 phone(e){this.setData({phone:e.detail.value})},
 address(e){this.setData({address:e.detail.value})},
 toWorks(){wx.switchTab({url:'/pages/works/index'})},
 async submit(){
  if(this.data.busy||this.data.checkingOrder||this.data.orderLookupFailed)return
  if(this.data.existingOrder){this.openExistingOrder(false);return}
  if(!this.data.shippingLoaded||this.data.shippingSaving)return
  this.setData({busy:true})
  try{
   const order=await request('POST','/api/orders',{
    work_id:this.data.id,name:this.data.name,phone:this.data.phone,address:this.data.address
   })
   wx.redirectTo({url:'/pages/order/index?id='+order.id})
  }catch(e){
   if(e.statusCode===409){
    const existing=await this.loadExistingOrder()
    if(existing){this.openExistingOrder(false);return}
   }
   tip(e)
  }finally{this.setData({busy:false})}
 }
})
