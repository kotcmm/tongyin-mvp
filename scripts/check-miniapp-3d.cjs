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
for(const s of ['type="webgl"','id="modelCanvas"','canvasTouchStart','canvasTouchMove','viewerLoading','viewerError']){
 assert(wxml.includes(s),'missing viewer UI: '+s)
}
for(const s of ['swiper','备用角度图','进入立体模型','<web-view','glb-viewer']){
 assert(!wxml.includes(s),'old viewer UI remains: '+s)
}
assert(!app.pages.includes('pages/viewer/index'),'old full screen viewer route remains')
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
console.log('Mini-program inline GLB viewer checks passed.')
