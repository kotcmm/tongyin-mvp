// Checks only HTTP method/path parity, not business behavior or readiness.
const fs = require('node:fs')
const path = require('node:path')
const main = fs.readFileSync(path.join(__dirname, '../backend/main.go'), 'utf8')
const go = [...main.matchAll(/mux\.HandleFunc\("([A-Z]+) (\/api[^"]+)"/g)]
  .map((m) => m[1] + ' ' + m[2])
const dir = path.join(__dirname, '../backend-dotnet/src/Tongyin.Api')
const files = []
function walk(p) {
  for (const item of fs.readdirSync(p, { withFileTypes: true })) {
    if (item.name === 'bin' || item.name === 'obj') continue
    if (item.isDirectory()) walk(path.join(p, item.name))
    else if (item.name.endsWith('.cs')) files.push(path.join(p, item.name))
  }
}
walk(dir)
const dotnet = files.flatMap((p) => {
  const body = fs.readFileSync(p, 'utf8')
  return [...body.matchAll(/\[Http(Get|Post|Put|Delete)\("(\/api[^"]+)"\)\]/g)]
    .map((m) => m[1].toUpperCase() + ' ' + m[2])
})
const missing = go.filter((route) => !dotnet.includes(route))
const extra = dotnet.filter((route) => !go.includes(route))
const duplicate = dotnet.filter((route, i) => dotnet.indexOf(route) !== i)
if (missing.length || extra.length || duplicate.length) {
  console.error({missing, extra, duplicate})
  process.exit(1)
}
console.log('API surface matched:', go.length, 'Go routes -> ASP.NET Core controller actions. Behavioral compatibility must be checked separately.')
