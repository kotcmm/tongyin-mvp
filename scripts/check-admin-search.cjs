const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert')
const html=fs.readFileSync(path.resolve(__dirname,'../backend/webadmin/index.html'),'utf8')
const script=['core.js','views.js','actions.js'].map(name=>
 fs.readFileSync(path.resolve(__dirname,'../backend/webadmin/'+name),'utf8')
).join('\n')
assert(html.includes('/admin/core.js') && html.includes('/admin/views.js') && html.includes('/admin/actions.js'),'admin script must be served by Go')
new Function(script)
assert(!script.includes('state.works.filter('),'browser still filtering works')
assert(!script.includes('state.orders.filter('),'browser still filtering orders')
assert(!script.includes('state.users.filter('),'browser still filtering users')
assert(/setTimeout\(\s*\(\)\s*=>\s*fetchListPage\(\),\s*300\s*\)/.test(script),'search debounce missing')
assert(/seq\s*!==\s*state\.searchSeq/.test(script),'stale response guard missing')
let bodyWrites=0
const els={}
function element(id){
 if(els[id])return els[id]
 let raw=''
 const o={
  id,value:'',classList:{add(){},remove(){},contains(){return false}},
  get innerHTML(){return raw},
  set innerHTML(v){
   raw=v
   if(id==='body'){
    bodyWrites++
    if(v.includes('id="keyword"'))els.keyword=elementFactory('keyword')
    if(v.includes('id="listResults"'))els.listResults=elementFactory('listResults')
   }
  },
  set textContent(v){this._text=String(v)},
  get textContent(){return this._text}
 }
 els[id]=o;return o
}
function elementFactory(id){
 const el={id,value:'',_html:'',classList:{add(){},remove(){},contains(){return false}},
   get innerHTML(){return this._html},set innerHTML(s){this._html=s},
   set textContent(v){this._text=String(v)},get textContent(){return this._text}}
 return el
}
const urls=[]
const pendingTimers=[]
const sandbox={
 document:{getElementById:element},
 sessionStorage:{getItem(){return ''},setItem(){},removeItem(){}},
 setTimeout(fn){pendingTimers.push(fn);return pendingTimers.length},
 clearTimeout(){},
 URLSearchParams,Date,console,confirm(){return true},alert(){},prompt(){return ''},
 fetch:async(url)=>{
  urls.push(url)
  const qs=new URL('http://example.test'+url).searchParams
  const q=qs.get('q')||''
  const page=Number(qs.get('page'))
  const route=url.split('?')[0]
  const items=route.endsWith('/works')?[{id:'model-'+q+'-'+page,owner_user_id:'user1',status:'model_ready',created_at:'2026-10-14T10:00:00+08:00',selected:0,candidates:[],preview:[],model:'',original:''}]:[]
  return {ok:true,status:200,json:async()=>({items,total:41,page,page_size:12})}
 }
}
const context=vm.createContext(sandbox)
vm.runInContext(script,context)
;(async()=>{
 vm.runInContext("state.tab='works';setupListPage()",context)
 const initialField=els.keyword
 assert(initialField,'search field must exist')
 const before=bodyWrites
 vm.runInContext("setSearch('mo')",context)
 assert.strictEqual(els.keyword,initialField,'typing replaced the search input')
 assert.strictEqual(bodyWrites,before,'typing should not rebuild the page')
 assert.strictEqual(pendingTimers.length,1,'debounce must schedule request')
 await pendingTimers.shift()()
 // Async timer callback returns undefined; flush microtasks, then ensure response
 await new Promise(resolve=>setImmediate(resolve))
 assert(urls.some(u=>u.includes('q=mo')),'server request missing search parameter')
 assert(els.listResults.innerHTML.includes('model-mo-1'),'server data not rendered')
 assert.strictEqual(els.keyword,initialField,'response replaced search input and lost focus')
 vm.runInContext('pageBy(1)',context)
 await new Promise(resolve=>setImmediate(resolve))
 assert(urls.some(u=>u.includes('page=2')),'backend pagination not requested')
 assert.strictEqual(els.keyword,initialField,'pagination unexpectedly replaced toolbar')
 vm.runInContext("setSearch('new')",context)
 assert.strictEqual(els.keyword,initialField,'multiple keystrokes lost focus')
 await pendingTimers.shift()()
 await new Promise(resolve=>setImmediate(resolve))
 assert(urls.some(u=>u.includes('q=new')&&u.includes('page=1')),'new query must reset to first page')
 vm.runInContext("openTab('shipping')",context)
 await new Promise(resolve=>setImmediate(resolve))
 assert(urls.some(u=>u.includes('category=shipping')),'shipping filter not sent to server')
 console.log('Admin search: focus retained, debounce, SQL-backed endpoint, paging and shipping filter verified.')
})().catch(e=>{console.error(e);process.exitCode=1})
