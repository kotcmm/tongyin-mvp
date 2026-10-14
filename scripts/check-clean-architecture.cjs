// Reject accidental outer-layer dependencies inside Domain / Application.
const fs=require('node:fs');
const path=require('node:path');
const root=path.join(__dirname,'../backend-dotnet/src');
const domain=path.join(root,'Tongyin.Domain');
const app=path.join(root,'Tongyin.Application');
const banned=[
  /\\bHttpContext\\b/,/\\bIFormFile\\b/,/\\bIResult\\b/,
  /\\bJsonObject\\b/,/\\bIStore\\b/,/\\bSqlConnection\\b/,
  /\\bNpgsql\\b/,/\\bMySqlConnector\\b/,
  /Microsoft\\.AspNetCore/,/Tongyin\\.Api/,/Tongyin\\.Infrastructure/
];
let bad=[];
for(const folder of [domain,app]){
  for(const f of walk(folder)){
    const txt=fs.readFileSync(f,'utf8');
    for(const re of banned)if(re.test(txt))bad.push(path.relative(root,f)+': '+re.source);
  }
}
function walk(dir){
  return fs.readdirSync(dir,{withFileTypes:true}).flatMap(entry=>{
    const p=path.join(dir,entry.name);
    return entry.isDirectory()?walk(p):entry.name.endsWith('.cs')?[p]:[];
  });
}
const project=(name)=>fs.readFileSync(path.join(root,name,name+'.csproj'),'utf8');
const d=project('Tongyin.Domain'),a=project('Tongyin.Application'),i=project('Tongyin.Infrastructure');
if(d.includes('ProjectReference')||d.includes('PackageReference'))bad.push('Domain must have no project/package refs');
if(!a.includes('../Tongyin.Domain/')||a.includes('../Tongyin.Infrastructure/')||a.includes('../Tongyin.Api/'))bad.push('Application reference direction invalid');
if(!i.includes('../Tongyin.Application/')||!i.includes('../Tongyin.Domain/')||i.includes('../Tongyin.Api/'))bad.push('Infrastructure reference direction invalid');
if(bad.length){console.error('Clean Architecture boundary violations:',bad);process.exit(1);}
console.log('PASS: Domain and Application do not reference HTTP, legacy JSON, storage, or outer projects.');
