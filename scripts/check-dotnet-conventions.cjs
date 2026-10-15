'use strict';
// Check user-authored C# against backend-dotnet/AGENTS.md; not a C# compiler.
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '../backend-dotnet/src');
const chinese = /[\u3400-\u9fff]/;
const pascalCase = /^[A-Z][A-Za-z0-9]*$/;
const placeholderDocs = [
  /业务参数/,
  /执行(?:new|undefined)业务操作/,
  /提供[A-Za-z_]\w*(?:所需的数据或操作|对应的业务操作)/,
];

function skipDirectory(name) {
  return name === 'bin' || name === 'obj' || name === '.git';
}

function walk(folder) {
  return fs.readdirSync(folder, { withFileTypes: true }).flatMap(item => {
    const fullPath = path.join(folder, item.name);
    if (item.isDirectory()) return skipDirectory(item.name) ? [] : walk(fullPath);
    return item.name.endsWith('.cs') ? [fullPath] : [];
  });
}

function hasChineseSummary(lines, index) {
  let j = index - 1;
  while (j >= 0 && /^\s*\[(?:ApiController|HttpGet|HttpPost|HttpPut|HttpDelete|Route|Authorize|From\w+)\b/.test(lines[j])) j--;
  while (j >= 0 && /^\s*\/\/\/ <param name=/.test(lines[j])) j--;
  if (!/^\s*\/\/\/ <\/summary>/.test(lines[j] || '')) return false;
  const docs = lines.slice(Math.max(0, j - 10), j).join('\n');
  return docs.includes('/// <summary>') && chinese.test(docs);
}

function analyzeSource(source, relative) {
  const errors = [];
  const lines = source.split(/\r?\n/);
  const typeRegex = /^(?:public|internal)\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:class|interface|record|enum|struct)\s+(\w+)/gm;
  const types = [...source.matchAll(typeRegex)].map(m => m[1]);
  for (const type of types) {
    if (!pascalCase.test(type)) errors.push(relative + ': public/internal type must be PascalCase: ' + type);
  }
  if (types.length !== 1 || path.basename(relative, '.cs') !== types[0]) {
    errors.push(relative + ': expected exactly one public/internal type matching filename, found ' + types.join(', '));
  }
  const isEnum = /\b(?:public|internal) enum\s+/.test(source);
  const isInterface = /\b(?:public|internal) interface\s+/.test(source);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trim = line.trim();
    const isPublic = /^(?:public|internal)\s+/.test(trim);
    const typeDecl = isPublic && /\b(?:class|interface|record|enum|struct)\s+\w+/.test(trim);
    const memberDecl = isPublic && !typeDecl && (trim.includes('(') || trim.includes('{') || trim.includes('=>'));
    const interfaceMethod = isInterface && /^(?:Task(?:<.*>)?|ValueTask(?:<.*>)?|void|string)\s+\w+(?:<.*>)?\s*\(/.test(trim);
    const enumMember = isEnum && /^([A-Za-z_]\w*)(?:\s*=\s*[^,]+)?\s*,?$/.test(trim) && !trim.startsWith('///');
    if ((typeDecl || memberDecl || interfaceMethod || enumMember) && !hasChineseSummary(lines, i)) {
      errors.push(relative + ':' + (i + 1) + ': missing Chinese XML <summary> for ' + trim.slice(0, 80));
    }
    if (enumMember) {
      const name = trim.match(/^([A-Za-z_]\w*)/)[1];
      if (!pascalCase.test(name)) errors.push(relative + ':' + (i + 1) + ': enum member must be PascalCase: ' + name);
    }
    if (memberDecl) {
      // Only inspect the declaration header; => new(...) and lambdas belong to the body.
      const header = trim.split('=>', 1)[0].split('{', 1)[0].trim();
      const completeMethod = header.match(/\b([A-Za-z_]\w*)\s*\([^()]*\)\s*$/);
      const multilineMethod = !completeMethod && header.match(/\b([A-Za-z_]\w*)\s*\([^)]*$/);
      const property = !completeMethod && !multilineMethod ? header.match(/\b([A-Za-z_]\w*)\s*$/) : null;
      const name = (completeMethod && completeMethod[1]) || (multilineMethod && multilineMethod[1]) || (property && property[1]);
      if (name && !pascalCase.test(name)) errors.push(relative + ':' + (i + 1) + ': public/internal member must be PascalCase: ' + name);
      if (multilineMethod) errors.push(relative + ':' + (i + 1) + ': keep method parameters on the declaration line: ' + name);
    }
    if (trim.startsWith('///') && placeholderDocs.some(pattern => pattern.test(trim))) {
      errors.push(relative + ':' + (i + 1) + ': non-descriptive XML comment: ' + trim);
    }
    const field = line.match(/^\s*private\s+(?:(?:static|readonly|volatile|const)\s+)*[\w.<>,?\[\]]+\s+([A-Za-z_]\w*)\s*(?:=(?!>)|;)/);
    if (field && !/^_[a-z][A-Za-z0-9]*$/.test(field[1])) {
      errors.push(relative + ':' + (i + 1) + ': private field must use _camelCase: ' + field[1]);
    }
  }
  return errors;
}

function main() {
  const errors = walk(root).flatMap(file => analyzeSource(fs.readFileSync(file, 'utf8'), path.relative(root, file)));
  if (errors.length) {
    console.error('C# conventions check failed (' + errors.length + '):\n' + errors.join('\n'));
    process.exitCode = 1;
    return;
  }
  console.log('PASS: C# type files, Chinese XML docs, PascalCase and private field names satisfy AGENTS.md.');
}

if (require.main === module) main();
module.exports = { analyzeSource, skipDirectory };
