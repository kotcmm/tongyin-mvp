// Smoke-test the generated mini-program endpoint while keeping the tracked
// config file untouched. PowerShell syntax is checked separately on Windows CI.
const fs=require('fs')
const path=require('path')
const assert=require('assert')
const root=path.resolve(__dirname,'..')
const config=fs.readFileSync(path.join(root,'miniprogram/config.js'),'utf8')
const script=fs.readFileSync(path.join(root,'scripts/dev.ps1'),'utf8')
const rx=/(API_BASE\s*:\s*['"])https?:\/\/[^'"]+(['"])/m
assert(rx.test(config),'config.js must define a static API_BASE')
function generate(url){
 return config.replace(rx,(_match,prefix,suffix)=>prefix+url+suffix)
}
const local=generate('http://localhost:18080')
const phone=generate('http://192.168.1.20:18080')
const customPort=generate('http://10.0.0.15:18081')
for(const [modified,url] of [[local,'http://localhost:18080'],[phone,'http://192.168.1.20:18080'],[customPort,'http://10.0.0.15:18081']]){
 assert(modified.includes("API_BASE: '"+url+"'"),'incorrect generated API_BASE: '+url)
 assert(modified.includes('DEV_LOGIN: true'),'must preserve dev login')
}
assert(phone!==config,'phone build must update default API address')
for(const s of [
 'Get-NetRoute','Get-NetIPAddress','Test-PrivateIPv4',
 '-Phone','-LanIP','LISTEN_ADDR','PUBLIC_URL','API_BASE',
 'WriteAllText($configFile, $original',
 'http://','/health'
]){
 assert(script.includes(s),'missing phone development feature: '+s)
}
assert(script.includes('DEV_LOGIN 共用测试身份'),'phone mode security warning missing')
assert(script.includes('Get-NetAdapter'),'route must resolve an actual network adapter')
console.log('Local/phone API origin configuration checks passed.')
