'use strict';
// Run the real conventions CLI against disposable C# fixtures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const checker = path.join(__dirname, 'check-dotnet-conventions.cjs');
const summary = '/// <summary>\n/// 中文业务说明。\n/// </summary>\n';

function runFixture(cases) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'tongyin-lint-'));
  try {
    const scriptDir = path.join(root, 'scripts');
    fs.mkdirSync(scriptDir);
    fs.copyFileSync(checker, path.join(scriptDir, 'check-dotnet-conventions.cjs'));
    for (const [file, content] of Object.entries(cases)) {
      const target = path.join(root, 'backend-dotnet', 'src', file);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, content);
    }
    return spawnSync(process.execPath, [path.join(scriptDir, 'check-dotnet-conventions.cjs')], { encoding: 'utf8' });
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

const valid = runFixture({
  'Tongyin.Test/Foo.cs': summary + 'public sealed class Foo\n{\n' +
    summary + 'public JsonObject Error(int code) => new(code);\n' +
    summary + 'public Task<Order> PlaceAsync(Order order) => store.Update(s=>s.Orders[order.Id]);\n' +
    summary + 'public Work ToDomain(JsonObject raw) => Work.Restore(new WorkState(\n  raw["id"],raw["owner"]));\n' +
    summary + 'public Task<(string Name,string Role)?> VerifyAdmin(HttpContext ctx) => null;\n' +
    '}\n',
  'Tongyin.Test/obj/Debug/net10.0/Generated.cs': 'global using System;\n',
  'Tongyin.Test/bin/Debug/net10.0/Generated.cs': 'public class Generated {}\n',
});
assert.equal(valid.status, 0, valid.stdout + valid.stderr);

const badName = runFixture({ 'Tongyin.Test/Foo.cs': summary + 'public class Foo\n{\n' + summary + 'public string badName() => "value";\n}' });
assert.notEqual(badName.status, 0);
assert.match(badName.stderr, /must be PascalCase: badName/);

const badSignature = runFixture({ 'Tongyin.Test/Foo.cs': summary + 'public class Foo\n{\n' + summary + 'public void Send(\n    string owner) {}\n}' });
assert.notEqual(badSignature.status, 0);
assert.match(badSignature.stderr, /keep method parameters on the declaration line/);

const noDoc = runFixture({ 'Tongyin.Test/Foo.cs': summary + 'public class Foo\n{\npublic string Get() => "x";\n}' });
assert.notEqual(noDoc.status, 0);
assert.match(noDoc.stderr, /missing Chinese XML/);
console.log('PASS: C# conventions CLI regression fixtures.');
