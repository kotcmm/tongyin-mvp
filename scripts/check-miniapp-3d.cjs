// Repository-level smoke checks; no WeChat runtime or developer tool required.
const fs=require('fs')
const vm=require('vm')
const assert=require('assert')
const path=require('path')
const root=path.resolve(__dirname,'..')
const read=p=>fs.readFileSync(path.join(root,p),'utf8')
const wxml=read('miniprogram/pages/preview/index.wxml')
const app=JSON.parse(read('miniprogram/app.json'))
const config=JSON.parse(read('miniprogram/pages/preview/index.json'))
const pageSource=read('miniprogram/pages/preview/index.js')
const admin=read('admin/index.html')
for(const s of ['type="webgl"','id="modelCanvas"','canvasTouchStart','canvasTouchMove','viewerLoading','viewerError','toggleFullscreen','fullscreen-gallery','model-toolbar','zoomIn','zoomOut','resetView']){
 assert(wxml.includes(s),'missing viewer UI: '+s)
}
for(const s of ['swiper','备用角度图','进入立体模型','<web-view','glb-viewer']){
 assert(!wxml.includes(s),'old viewer UI remains: '+s)
}
assert(!app.pages.includes('pages/viewer/index'),'old full screen viewer route remains')
assert(wxml.includes('<page-meta page-style='),'full screen page scroll lock missing')
assert((wxml.match(/id="modelCanvas"/g)||[]).length===1,'full screen must reuse a single canvas')
assert(wxml.includes('退出全屏'),'missing close-fullscreen action')
assert(!config.usingComponents,'native XR Frame dependency remains')
assert(!admin.includes('renderExisting('),'admin still requires static preview generator')
for(const p of ['miniprogram/vendor/threejs-miniprogram/index.js','miniprogram/vendor/threejs-miniprogram/gltf-loader.js','miniprogram/vendor/threejs-miniprogram/LICENSE']){
 assert(fs.existsSync(path.join(root,p)),'vendor asset missing: '+p)
}
let page
vm.runInNewContext(pageSource,{
 Page:opts=>{page=opts},
 require:name=>{
  if(name.endsWith('utils/api'))return {request:()=>Promise.resolve({}),tip:()=>{}}
  if(name.endsWith('/index'))return {createScopedThreejs:()=>{}}
  return {registerGLTFLoader:()=>{}}
 },
 wx:{},
 setInterval:()=>0,clearInterval:()=>{}
})
assert(page && typeof page.canvasTouchMove==='function','Page controls missing')
const test=Object.assign({},page,{
 _model:{rotation:{x:0,y:0}},
 _camera:{zoom:1,updateProjectionMatrix(){this.updated=true}},
 paintViewer(){}
})
test.canvasTouchStart({touches:[{clientX:20,clientY:40}]})
test.canvasTouchMove({touches:[{clientX:60,clientY:50}]})
assert(test._model.rotation.y>0,'one finger must rotate the model')
test.canvasTouchStart({touches:[{clientX:10,clientY:10},{clientX:20,clientY:10}]})
test.canvasTouchMove({touches:[{clientX:10,clientY:10},{clientX:30,clientY:10}]})
assert(test._camera.zoom>1,'two fingers must zoom camera')
// Fullscreen must resize the same WebGL canvas, without resetting the model
// rotation, zoom, or starting a second model download.
let size={width:320,height:260}, resized=[], repaints=0, downloads=0
const fakeRenderer={setSize(w,h){resized.push({w,h})}}
const fullscreen=Object.assign({},page,{
 data:{fullscreen:false},
 _renderer:fakeRenderer,
 _camera:{zoom:2,updateProjectionMatrix(){}},
 _model:{rotation:{x:.2,y:.6}},
 _loadedKey:'unchanged.glb',
 setData(x,cb){this.data=Object.assign({},this.data,x);if(cb)cb()},
 paintViewer(){repaints++},
 prepareViewer(){downloads++},
})
const mockWx={
 nextTick(fn){fn()},
 createSelectorQuery(){
  let callback;
  return {
   select(selector){assert.strictEqual(selector,'#modelCanvas');return this},
   boundingClientRect(fn){callback=fn;return this},
   exec(){callback(size)}
  }
 }
}
// Patch the lexical wx used by the page methods by evaluating in a fresh VM.
let fullscreenPage
vm.runInNewContext(pageSource,{
 Page:opts=>fullscreenPage=opts,
 require:name=>name.endsWith('utils/api')?{request(){},tip(){}}:
   name.endsWith('/index')?{createScopedThreejs(){}}:{registerGLTFLoader(){}},
 wx:mockWx,setInterval(){return 1},clearInterval(){}
})
const active=Object.assign(fullscreen,fullscreenPage,{
 data:{fullscreen:false},setData:fullscreen.setData,_renderer:fakeRenderer,
 _camera:fullscreen._camera,_model:fullscreen._model,_loadedKey:fullscreen._loadedKey,
 paintViewer:fullscreen.paintViewer,prepareViewer:fullscreen.prepareViewer
})
active.toggleFullscreen()
assert.strictEqual(active.data.fullscreen,true,'maximize did not open')
assert.strictEqual(resized[0].w,320)
assert.strictEqual(resized[0].h,260)
size={width:300,height:400}
active.zoomIn()
assert(active._camera.zoom>2,'zoom in not working')
active.resetView()
assert.strictEqual(active._camera.zoom,1,'reset zoom failed')
assert.strictEqual(active._model.rotation.y,0,'reset rotation failed')
active.toggleFullscreen()
assert.strictEqual(active.data.fullscreen,false,'maximize did not close')
assert.strictEqual(resized[1].h,400,'canvas was not resized when closing')
assert.strictEqual(active._loadedKey,'unchanged.glb','fullscreen changed model key')
assert.strictEqual(downloads,0,'fullscreen must not reload the GLB')
assert(repaints>=4,'fullscreen must repaint on resize and controls')
console.log('Mini-program inline GLB viewer and fullscreen checks passed.')
