'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const base = path.join(__dirname, '..', 'backend-dotnet', 'src', 'Tongyin.Api', 'wwwroot', 'admin');
const js = fs.readFileSync(path.join(base, 'orders.js'), 'utf8');
const html = fs.readFileSync(path.join(base, 'index.html'), 'utf8');

assert.match(html, /data-tab="works"/);
assert.match(html, /图片生成任务/);
assert.match(html, /3D 模型生成任务/);
assert.doesNotMatch(html, /超时图片任务|3D 模型异常/);
assert.match(js, /\/api\/admin\/ai\/image-tasks/);
assert.match(js, /\/api\/admin\/ai\/model-tasks/);

const nodes = new Map();
function element(id) {
  if (!nodes.has(id)) {
    nodes.set(id, {
      id, innerHTML: '', textContent: '', value: '', hidden: false, disabled: false,
      classList: { toggle() {}, add() {}, remove() {} }
    });
  }
  return nodes.get(id);
}

const window = {
  location: { hash: '#works' },
  addEventListener() {}, setTimeout() {}, setInterval() {}
};
const document = {
  hidden: false, addEventListener() {}, querySelectorAll() { return []; },
  getElementById: element
};
const ctx = vm.createContext({ window, document, console, URL, decodeURIComponent });
vm.runInContext(js, ctx, { filename: 'admin/orders.js' });
assert.equal(vm.runInContext('tabFromHash()', ctx), 'works');
vm.runInContext('openTab("review")', ctx);
assert.equal(window.location.hash, '#review', 'navigation must change URL hash');
assert.equal(element('pageTitle').textContent, '图片生成任务');
assert.ok(element('statusFilter').innerHTML.includes('排队中'));
assert.ok(element('statusFilter').innerHTML.includes('超时待核查'));
vm.runInContext('openTab("model-review")', ctx);
assert.equal(window.location.hash, '#model-review');
assert.equal(element('pageTitle').textContent, '3D 模型生成任务');
window.location.hash = '#works';
assert.equal(vm.runInContext('tabFromHash()', ctx), 'works', 'refresh restores previous tab');
window.location.hash = '#%unsafe';
assert.equal(vm.runInContext('tabFromHash()', ctx), 'orders', 'unrecognized hash falls back safely');

vm.runInContext(`
  state.tab = 'review';
  state.reviews = [
    { work_id: 'work-1', generation_id: 'gen-1', status: 'queued',
      api_phase: 'not_dispatched', created_at: '2026-10-19', started_at: '',
      provider_id: '', prompt_version: 0, note: '', can_review: false, image_attempts: 1 },
    { work_id: 'work-2', generation_id: 'gen-2', status: 'stalled',
      api_phase: 'claimed_outcome_unconfirmed', created_at: '2026-10-19',
      started_at: '2026-10-19T08:00:00+08:00', provider_id: 'provider-2',
      prompt_version: 2, note: '超时待核查', can_review: true, image_attempts: 2 }
  ];
  render();
`, ctx);
let output = element('listResults').innerHTML;
assert.match(output, /<table/);
assert.match(output, /排队中/);
assert.match(output, /超时待核查/);
assert.match(output, /服务商接收／结果尚未确认/);
assert.match(output, /data-action="review-fail"/);
assert.equal((output.match(/data-action="review-fail"/g) || []).length, 1,
  'review action only visible for eligible stalled tasks');
element('statusFilter').value = 'queued';
vm.runInContext('render()', ctx);
output = element('listResults').innerHTML;
assert.match(output, /work-1/);
assert.doesNotMatch(output, /work-2/, 'status filter must exclude other rows');

vm.runInContext(`
  state.tab = 'model-review';
  state.modelReviews = [{ work_id: 'model-1', generation_id: 'g-model', status: 'completed',
    api_phase: 'result_saved', created_at: '2026-10-19', started_at: '',
    provider_id: '3d-worker', prompt_version: 1, note: '', can_review: false }];
`, ctx);
element('statusFilter').value = '';
vm.runInContext('render()', ctx);
assert.match(element('listResults').innerHTML, /model-1/);
assert.match(element('listResults').innerHTML, /结果已保存到童印/);
console.log('Admin tab persistence and image/model task list rendering checks passed.');
