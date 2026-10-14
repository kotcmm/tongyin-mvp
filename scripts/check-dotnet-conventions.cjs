// Validate the project's AGENTS.md conventions without changing source files.
// Deliberately conservative: verify declarations with a stable lexical shape.
const fs=require('node:fs');
const path=require('node:path');
const root=path.join(__dirname,'../backend-dotnet/src');
const errors=[];
function walk(dir){
  return fs.readdirSync(dir,{withFileTypes:true}).flatMap(entry=>{
    const name=path.join(dir,entry.name);
    if(entry.isDirectory())return walk(name);
    return entry.name.endsWith('.cs')?[name]:[];
  });
}
const cn=/[\u3400-\u9fff]/;
function hasXml(lines,index){
  let at=index-1;
  while(at>=0 && /^\s*\[(?:ApiController|HttpGet|HttpPost|HttpPut|HttpDelete|Route|Authorize|From\w+)\b/.test(lines[at]))at--;
  while(at>=0 && /^\s*\/\/\/ <param name=/.test(lines[at]))at--;
  if(!/^\s*\/\/\/ <\/summary>/.test(lines[at]||''))return false;
  const preceding=lines.slice(Math.max(0,at-5),at).join('\n');
  return preceding.includes('/// <summary>') && cn.test(preceding);
}
for(const file of walk(root)){
  const src=fs.readFileSync(file,'utf8'),lines=src.split(/\r?\n/);
  const relative=path.relative(root,file);
  const types=[...src.matchAll(/^(?:public|internal)\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:class|interface|record|enum|struct)\s+(\w+)/gm)].map(m=>m[1]);
  for(const type of types)
    if(!/^[A-Z][A-Za-z0-9]*$/.test(type))
      errors.push(relative+': public/internal type must be PascalCase: '+type);
  if(types.length!==1 || path.basename(file,'.cs')!==types[0]){
    errors.push(relative+': one public/internal type per same-named C# file required; found '+types.join(', '));
  }
  const isEnum=/\b(?:public|internal) enum\s+/.test(src),isInterface=/\b(?:public|internal) interface\s+/.test(src);
  for(let i=0;i<lines.length;i++){
    const line=lines[i],trim=line.trim();
    const decl=/^(?:public|internal)\s+/.test(trim) &&
      (/(?:\(|\{|=>)/.test(trim) || /\b(?:class|interface|record|enum|struct)\s+\w+/.test(trim));
    const interfaceMember=isInterface && /^(?:Task(?:<.*>)?|ValueTask(?:<.*>)?|void|string)\s+\w+(?:<.*>)?\s*\(/.test(trim);
    const enumMember=isEnum && /^\s+[A-Z]\w*(?:\s*=\s*\d+)?\s*,?\s*$/.test(line) && !/^\s+(?:public|internal)/.test(line);
    // Public C# members use PascalCase. Record positional parameter names
    // are constructor inputs rather than public symbol declarations.
    if(decl && !/\\b(?:class|interface|record|enum|struct)\\s+\\w+/.test(trim)){
      const names=[...trim.matchAll(/([A-Za-z_]\\w*)(?:<[^()]*>)?\\s*\\(/g)];
      const methodName=names.length ? names[names.length-1][1] : null;
      const propertyName=trim.match(/\\b([A-Za-z_]\\w*)\\s*(?:\\{|=>)/)?.[1];
      const name=methodName||propertyName;
      if(name && !/^[A-Z][A-Za-z0-9]*$/.test(name))
        errors.push(relative+':'+(i+1)+': public/internal member must be PascalCase: '+name);
      if(/\\b(public|internal)\\b/.test(trim) && /\\w+\\s*\\($/.test(trim))
        errors.push(relative+':'+(i+1)+': keep method parameters on the declaration line');
    }
    if((decl||interfaceMember||enumMember) && !hasXml(lines,i))
      errors.push(relative+':'+(i+1)+': missing Chinese XML <summary> for '+trim.slice(0,80));
    const field=line.match(/^\s*private\s+(?:(?:static|readonly|volatile|const)\s+)*[\w.<>,?\[\]]+\s+([A-Za-z_]\w*)\s*(?:=(?!>)|;)/);
    if(field && !/^_[a-z][A-Za-z0-9]*$/.test(field[1]))
      errors.push(relative+':'+(i+1)+': private field must use _camelCase: '+field[1]);
  }
}
if(errors.length){
  console.error('C# conventions check failed ('+errors.length+'):\n'+errors.join('\n'));
  process.exit(1);
}
console.log('PASS: C# type files, Chinese XML docs and private field names satisfy AGENTS.md.');
