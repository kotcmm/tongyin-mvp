'use strict';
// .NET 履约后台：仅提供当前已经迁移的订单制作与发货能力，避免出现旧 Go 页面上的失效操作。
const $ = (id) => document.getElementById(id);
const h = (value) => String(value ?? '').replace(/[&<>"']/g, (c) =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const statusLabels = {
  awaiting_payment: '待付款', paid: '已支付', printing: '制作中',
  shipped: '已发货', completed: '已完成', canceled: '已取消'
};
const state = { authenticated: false, csrf: '', username: '', tab: 'orders', aiStage: 'image', orders: [], works: [], reviews: [], modelReviews: [], page: 1, pageSize: 12, busy: false };

async function refreshCsrf() {
  const response = await fetch('/api/admin/account/csrf', { credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new Error('无法获取安全验证令牌');
  const result = await response.json();
  state.csrf = result.token;
}

async function api(path, options = {}) {
  const method = (options.method || 'GET').toUpperCase();
  if (method !== 'GET' && method !== 'HEAD' && !state.csrf) await refreshCsrf();
  const headers = {
    ...(method !== 'GET' && method !== 'HEAD' ? { 'X-CSRF-TOKEN': state.csrf } : {}),
    ...(options.headers || {})
  };
  const response = await fetch(path, { ...options, headers, cache: 'no-store', credentials: 'same-origin' });
  const data = response.status === 204 ? {} : await response.json().catch(() => ({ error: '服务器响应格式异常' }));
  if (!response.ok) {
    const error = new Error(data.error || ('请求失败 HTTP ' + response.status));
    error.status = response.status;
    throw error;
  }
  return data;
}

function toast(message) {
  $('toast').textContent = message;
  $('toast').classList.remove('hidden');
  window.setTimeout(() => $('toast').classList.add('hidden'), 3500);
}

async function logout(revoke = false) {
  if (revoke && state.authenticated) {
    try { await api('/api/admin/account/logout', { method: 'POST' }); }
    catch (error) { toast(error.message); }
  }
  state.authenticated = false;
  state.csrf = '';
  state.username = '';
  state.orders = [];
  state.works = [];
  state.reviews = [];
  state.modelReviews = [];
  $('adminPassword').value = '';
  $('currentAdminPassword').value = '';
  $('newAdminPassword').value = '';
  $('confirmAdminPassword').value = '';
  $('app').classList.add('hidden');
  $('loginWrap').classList.remove('hidden');
  $('modalContainer').replaceChildren();
  $('adminUsername').focus();
}

function setBusy(value) {
  state.busy = value;
  $('refresh').disabled = value;
  $('loginSubmit').disabled = value;
}

async function reload() {
  if (!state.authenticated || state.busy || state.tab === 'account') return;
  if (['wechat-settings', 'image-settings', 'model-settings'].includes(state.tab)) {
    await loadConfiguration();
    return;
  }
  setBusy(true);
  $('pageMessage').textContent = state.tab === 'works' ? '正在读取最新作品…' :
    state.tab === 'review' ? '正在读取超时图片任务…' :
    state.tab === 'model-review' ? '正在读取 3D 模型超时任务…' : '正在读取最新订单…';
  try {
    const worksMode = state.tab === 'works';
    const reviewMode = state.tab === 'review';
    const modelReviewMode = state.tab === 'model-review';
    const route = modelReviewMode ? '/api/admin/ai/stalled-model-tasks' :
      reviewMode ? '/api/admin/ai/stalled-image-tasks' :
      worksMode ? '/api/admin/works' : '/api/admin/orders';
    const records = await api(route);
    if (!Array.isArray(records)) throw new Error('列表响应格式异常');
    if (modelReviewMode) state.modelReviews = records;
    else if (reviewMode) state.reviews = records;
    else if (worksMode) state.works = records;
    else state.orders = records;
    $('lastLoaded').textContent = new Date().toLocaleTimeString();
    $('pageMessage').textContent = '';
    render();
  } catch (error) {
    $('pageMessage').textContent = error.message;
    if (error.status === 401) logout(false);
    throw error;
  } finally {
    setBusy(false);
  }
}

async function login(event) {
  event.preventDefault();
  $('loginError').textContent = '';
  const username = $('adminUsername').value.trim();
  const password = $('adminPassword').value;
  if (!username || !password) return;
  $('loginSubmit').disabled = true;
  try {
    const result = await api('/api/admin/account/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });
    state.authenticated = true;
    state.username = result.username;
    await refreshCsrf();
    $('adminCurrentUser').textContent = '管理员：' + state.username;
    $('adminPassword').value = '';
    $('loginWrap').classList.add('hidden');
    $('app').classList.remove('hidden');
    await reload();
  } catch (error) {
    $('loginError').textContent = error.status === 429 ? '尝试过于频繁，请稍后再试' : error.message;
    state.authenticated = false;
    $('app').classList.add('hidden');
    $('loginWrap').classList.remove('hidden');
  } finally {
    $('loginSubmit').disabled = false;
  }
}

// 作品按真实状态及作品编号检索，不依赖微信 OpenID。
const workStatusLabels = {
  image_pending: '正在生成形象', image_ready: '形象待确认',
  model_pending: '正在生成模型', model_ready: '模型已完成', failed: '处理失败'
};

function filteredWorks() {
  const keyword = $('keyword').value.trim().toLowerCase();
  const chosenStatus = $('statusFilter').value;
  return state.works.filter((work) => {
    if (chosenStatus && work.status !== chosenStatus) return false;
    const fields = [work.id, work.status, work.owner_user_id, work.note, work.generation_id];
    return !keyword || fields.some((field) => String(field ?? '').toLowerCase().includes(keyword));
  });
}

function signedImage(url, alt) {
  if (!url) return '<span class="muted small">尚未生成</span>';
  const safe = h(url);
  return '<a target="_blank" rel="noopener noreferrer" href="' + safe + '">' +
    '<img class="preview" loading="lazy" alt="' + h(alt) + '" src="' + safe + '"></a>';
}

function workCard(work) {
  const selected = Number(work.selected);
  const candidates = Array.isArray(work.candidates) ? work.candidates : [];
  const previews = Array.isArray(work.preview) ? work.preview : [];
  const model = work.model
    ? '<a target="_blank" rel="noopener noreferrer" download href="' + h(work.model) + '">下载 GLB 模型</a>'
    : '<span class="muted small">尚未生成 GLB</span>';
  const note = work.note ? '<p class="small muted">备注：' + h(work.note) + '</p>' : '';
  const attempts = Number.isFinite(Number(work.image_attempts)) ? work.image_attempts : 0;
  return '<article class="card"><div class="card-header"><div><strong>作品 #' + h(work.id) +
    '</strong>　<span class="pill">' + h(workStatusLabels[work.status] || work.status) + '</span></div>' +
    '<span class="small muted">' + h(String(work.created_at || '').replace('T', ' ').slice(0, 19)) + '</span></div>' +
    '<p class="meta">图片生成次数：' + h(attempts) + '　·　已选形象：' +
    (selected >= 0 ? h(selected + 1) : '未确认') + '</p>' +
    '<div class="grid"><div class="step"><h4>① 原始画作</h4>' + signedImage(work.original, '儿童原始画作') + '</div>' +
    '<div class="step"><h4>② AI 形象</h4>' + signedImage(candidates[0] || '', 'AI 生成形象') + '</div>' +
    '<div class="step"><h4>③ GLB 白模</h4>' + (previews[0] ? signedImage(previews[0], '三维模型预览') + '<div>' + model + '</div>' : model) +
    '</div></div>' + note + '</article>';
}

// 只展示真实已认领且超时的图片任务，由服务端再次校验当前状态和任务编号。
function filteredReviews() {
  const keyword = $('keyword').value.trim().toLowerCase();
  return state.reviews.filter((task) => {
    const values = [task.work_id, task.generation_id, task.provider_id];
    return !keyword || values.some((value) => String(value ?? '').toLowerCase().includes(keyword));
  });
}

function reviewCard(task) {
  const workId = h(task.work_id);
  const generationId = h(task.generation_id);
  const started = h(String(task.started_at || '').replace('T', ' ').slice(0, 19));
  const provider = h(task.provider_id || '未记录');
  const legacy = task.is_legacy_go ? '旧 Go 任务' : '.NET 任务';
  return '<article class="card"><div class="card-header">' +
    '<strong>超时作品 #' + workId + '</strong><span class="pill warning">' + h(legacy) + '</span></div>' +
    '<p class="meta">任务编号：' + generationId + '</p>' +
    '<p class="meta">开始时间：' + started + '　·　服务商：' + provider +
    '　·　图片生成次数：' + h(task.image_attempts) + '</p>' +
    '<div class="row" style="margin-top:14px">' +
    '<button type="button" class="danger" data-action="review-fail" data-id="' + workId +
    '" data-generation="' + generationId + '">已核实失败</button>' +
    '<button type="button" data-action="review-complete" data-id="' + workId +
    '" data-generation="' + generationId + '">补录真实图片</button></div></article>';
}

function openReview(taskId, generationId, mode) {
  const complete = mode === 'review-complete';
  const title = complete ? '补录已生成图片' : '确认图片任务失败';
  const file = complete
    ? '<label for="reviewFile">服务商实际生成的图片</label>' +
      '<input id="reviewFile" name="file" type="file" accept="image/png,image/jpeg,image/webp" required>' +
      '<p class="small muted">仅支持 PNG/JPG/WebP，最大 10 MiB。不得上传与原任务无关的图片。</p>'
    : '';
  $('modalContainer').innerHTML = '<div class="modal-back">' +
    '<form class="modal" id="reviewForm">' +
    '<div class="card-header"><h2>' + title +
    '</h2><button type="button" class="ghost" data-close>关闭 ✕</button></div>' +
    '<p class="small muted">作品 ' + h(taskId) + ' · 当前任务 ' + h(generationId) +
    '。操作前必须核实服务商实际结果，系统会再次校验任务是否仍然超时。</p>' +
    '<label for="reviewReason">人工核实依据</label>' +
    '<textarea id="reviewReason" name="reason" rows="4" maxlength="300" required ' +
    'placeholder="如：已向服务商核查任务编号，确认无输出结果"></textarea>' +
    file + '<div class="row" style="margin-top:20px">' +
    '<button type="submit">' + (complete ? '确认补录' : '确认失败') + '</button></div>' +
    '</form></div>';
  const form = $('reviewForm');
  form.dataset.workId = taskId;
  form.dataset.generationId = generationId;
  form.dataset.mode = mode;
  $('reviewReason').focus();
}

async function submitReview(event) {
  if (event.target.id !== 'reviewForm') return;
  event.preventDefault();
  if (state.busy) return;
  const formNode = event.target;
  const workId = formNode.dataset.workId;
  const generationId = formNode.dataset.generationId;
  const reason = $('reviewReason').value.trim();
  const complete = formNode.dataset.mode === 'review-complete';
  if (!reason || reason.length > 300) {
    toast('需填写不超过 300 字的真实核实依据');
    return;
  }
  const file = complete ? $('reviewFile').files[0] : null;
  if (complete && (!file || file.size === 0 || file.size > 10 * 1024 * 1024)) {
    toast('请提供不超过 10 MiB 的真实生成图片');
    return;
  }
  const question = complete
    ? '已核实此图片确实是当前任务的服务商生成结果，并确认补录？'
    : '已核实服务商确定失败，并确认将当前任务标记失败？';
  if (!window.confirm(question)) return;
  const endpoint = '/api/admin/ai/stalled-image-tasks/' +
    encodeURIComponent(workId) + (complete ? '/complete' : '/fail');
  setBusy(true);
  try {
    let options;
    if (complete) {
      // 浏览器自动生成 multipart boundary；不得人为指定 Content-Type。
      const form = new FormData();
      form.append('generation_id', generationId);
      form.append('reason', reason);
      form.append('file', file);
      options = { method: 'POST', body: form };
    } else {
      options = {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ generation_id: generationId, reason })
      };
    }
    await api(endpoint, options);
    $('modalContainer').replaceChildren();
    toast(complete ? '图片已补录，作品进入待确认状态' : '已标记失败，操作已写入审计');
  } catch (error) {
    toast(error.message);
    if (error.status === 401) logout(false);
    return;
  } finally {
    setBusy(false);
  }
  await reload().catch((error) => toast(error.message));
}


// 模型异常列表仅展现已认领且超过阈值的任务，实际可处理条件由服务器在事务中再次核对。
function filteredModelReviews() {
  const keyword = $('keyword').value.trim().toLowerCase();
  return state.modelReviews.filter((task) => {
    const values = [task.work_id, task.generation_id, task.provider_id];
    return !keyword || values.some((item) => String(item ?? '').toLowerCase().includes(keyword));
  });
}

function modelReviewCard(task) {
  const workId = h(task.work_id);
  const generationId = h(task.generation_id);
  const started = h(String(task.started_at || '').replace('T', ' ').slice(0, 19));
  const legacy = task.is_legacy_go ? '旧 Go 任务' : '.NET 任务';
  return '<article class="card"><div class="card-header">' +
    '<strong>3D 异常作品 #' + workId + '</strong><span class="pill warning">' + h(legacy) + '</span></div>' +
    '<p class="meta">当前任务：' + generationId + '</p>' +
    '<p class="meta">开始时间：' + started +
    '　·　服务商：' + h(task.provider_id || '未记录') + '</p>' +
    '<div class="row" style="margin-top:14px">' +
    '<button type="button" class="danger" data-action="model-fail" data-id="' + workId +
    '" data-generation="' + generationId + '">确认模型失败</button>' +
    '<button type="button" data-action="model-complete" data-id="' + workId +
    '" data-generation="' + generationId + '">补录真实 GLB</button></div></article>';
}

function openModelReview(workId, generationId, action) {
  const complete = action === 'model-complete';
  const title = complete ? '补录实际生成的 GLB' : '核实模型生成失败';
  const fileControl = complete
    ? '<label for="modelFile">实际 GLB 模型文件（上限 100 MiB）</label>' +
      '<input id="modelFile" name="file" type="file" accept=".glb,model/gltf-binary" required>' +
      '<p class="small muted">必须是服务商已生成且归属当前任务的 glTF 2.0 GLB 文件。</p>'
    : '';
  $('modalContainer').innerHTML = '<div class="modal-back"><form id="modelReviewForm" class="modal">' +
    '<div class="card-header"><h2>' + title +
    '</h2><button type="button" class="ghost" data-close>关闭 ✕</button></div>' +
    '<p class="small muted">作品 ' + h(workId) + ' · 生成任务 ' + h(generationId) +
    '。先查询模型服务商真实结果；超过 15 分钟不等于任务失败。</p>' +
    '<label for="modelReviewReason">核实依据（必填）</label>' +
    '<textarea id="modelReviewReason" rows="4" maxlength="300" required ' +
    'placeholder="例如：在 Worker 日志中核对当前任务编号及实际结果"></textarea>' +
    fileControl +
    '<div class="row" style="margin-top:20px"><button type="submit">' +
    (complete ? '确认补录 GLB' : '确认失败') + '</button></div></form></div>';
  const form = $('modelReviewForm');
  form.dataset.workId = workId;
  form.dataset.generationId = generationId;
  form.dataset.complete = complete ? 'true' : 'false';
  $('modelReviewReason').focus();
}

async function submitModelReview(event) {
  if (event.target.id !== 'modelReviewForm') return;
  event.preventDefault();
  if (state.busy) return;
  const formNode = event.target;
  const complete = formNode.dataset.complete === 'true';
  const reason = $('modelReviewReason').value.trim();
  const generationId = formNode.dataset.generationId;
  const workId = formNode.dataset.workId;
  if (!reason || reason.length > 300) {
    toast('请填写不超过 300 字的核实依据');
    return;
  }
  const file = complete ? $('modelFile').files[0] : null;
  if (complete && (!file || file.size < 24 || file.size > 100 * 1024 * 1024)) {
    toast('请上传符合 GLB 格式且不超过 100 MiB 的实际模型文件');
    return;
  }
  if (!window.confirm(complete
    ? '已确认该 GLB 确实属于当前模型任务，是否补录？'
    : '已向服务商确认模型任务确实失败，是否登记失败？')) return;
  const endpoint = '/api/admin/ai/stalled-model-tasks/' +
    encodeURIComponent(workId) + (complete ? '/complete' : '/fail');
  setBusy(true);
  try {
    let options;
    if (complete) {
      const form = new FormData();
      form.append('generation_id', generationId);
      form.append('reason', reason);
      form.append('file', file);
      // 浏览器自动设置 multipart boundary；不得强制设置 Content-Type。
      options = { method: 'POST', body: form };
    } else {
      options = {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ generation_id: generationId, reason })
      };
    }
    await api(endpoint, options);
    $('modalContainer').replaceChildren();
    toast(complete ? '真实 GLB 已补录，作品进入模型就绪' : '模型失败已登记并审计');
  } catch (error) {
    toast(error.message);
    if (error.status === 401) logout(false);
    return;
  } finally {
    setBusy(false);
  }
  await reload().catch((error) => toast(error.message));
}

function filteredOrders() {
  const keyword = $('keyword').value.trim().toLowerCase();
  const chosenStatus = $('statusFilter').value;
  return state.orders.filter((o) => {
    if (state.tab === 'shipping' && !['printing', 'shipped', 'completed'].includes(o.status)) return false;
    if (chosenStatus && o.status !== chosenStatus) return false;
    const searchable = [o.id, o.work_id, o.name, o.carrier, o.tracking]
      .map((value) => String(value ?? '')).join(' ').toLowerCase();
    return !keyword || searchable.includes(keyword);
  });
}

function render() {
  const worksMode = state.tab === 'works';
  const reviewMode = state.tab === 'review';
  const modelReviewMode = state.tab === 'model-review';
  const rows = modelReviewMode ? filteredModelReviews() :
    reviewMode ? filteredReviews() : worksMode ? filteredWorks() : filteredOrders();
  const pageCount = Math.max(1, Math.ceil(rows.length / state.pageSize));
  state.page = Math.min(state.page, pageCount);
  const begin = (state.page - 1) * state.pageSize;
  $('listResults').innerHTML = rows.length
    ? rows.slice(begin, begin + state.pageSize).map(modelReviewMode ? modelReviewCard : reviewMode ? reviewCard : worksMode ? workCard : orderCard).join('')
    : '<div class="note">暂无符合条件的' + (modelReviewMode ? '超时模型任务' : reviewMode ? '超时任务' : worksMode ? '作品' : '订单') + '。</div>';
  $('pagerText').textContent = rows.length + ' 条 · 第 ' + state.page + '/' + pageCount + ' 页';
  $('previous').disabled = state.page <= 1;
  $('next').disabled = state.page >= pageCount;
}

function orderCard(o) {
  const id = h(o.id);
  const status = h(statusLabels[o.status] || o.status);
  const refund = o.refund_status
    ? '<span class="pill warning">退款处理中：' + h(o.refund_status) + '</span>' : '';
  let actions = '';
  if (o.status === 'paid' && !o.refund_status)
    actions = '<button type="button" data-action="printing" data-id="' + id + '">开始制作</button>';
  else if (o.status === 'printing' && !o.refund_status)
    actions = '<button type="button" data-action="shipment" data-id="' + id + '">录入发货</button>';
  else if (o.refund_status)
    actions = '<span class="small muted">存在退款申请，已暂停制作与发货</span>';
  const created = h(String(o.created_at || '').replace('T', ' ').slice(0, 19));
  const price = Number.isFinite(Number(o.price_fen)) ? (Number(o.price_fen) / 100).toFixed(2) : '--';
  const tracking = o.tracking
    ? '<div class="detail">物流：' + h(o.carrier || '快递') + ' · ' + h(o.tracking) +
      ' · ' + h(o.tracking_state || '尚未核实轨迹') + '</div>' : '';
  return '<article class="card"><div class="card-header"><div><strong>订单 #' + id +
    '</strong>　<span class="pill">' + status + '</span> ' + refund +
    '</div><strong>¥' + price + '</strong></div>' +
    '<p class="meta">作品 ' + h(o.work_id) + ' · ' + created + '</p>' +
    '<div class="detail">收件人：' + h(o.name) + '　' + h(o.phone) +
    '<div>地址：' + h(o.address) + '</div></div>' + tracking +
    '<div class="row" style="margin-top:14px">' + actions + '</div></article>';
}

const configurationTabs = ['wechat-settings', 'image-settings', 'model-settings'];

async function loadConfiguration() {
  if (!state.authenticated || state.busy || !configurationTabs.includes(state.tab)) return;
  setBusy(true);
  $('pageMessage').textContent = '正在读取配置…';
  try {
    if (state.tab === 'wechat-settings') {
      const settings = await api('/api/admin/system/wechat');
      $('wechatAppId').value = settings.app_id || '';
      $('wechatAppSecret').value = '';
      $('wechatKeyStatus').textContent = settings.has_app_secret ? '已配置' : '未配置';
      $('wechatSettingsError').textContent = '';
    } else {
      const stage = state.tab === 'image-settings' ? 'image' : 'model';
      state.aiStage = stage;
      const config = await api('/api/admin/ai/settings/' + stage);
      $('aiEndpoint').value = config.endpoint || '';
      $('aiModel').value = config.model || '';
      $('aiApiKey').value = '';
      $('aiKeyStatus').textContent = config.hasApiKey ? '已配置' : '未配置';
      $('aiEnabled').checked = Boolean(config.enabled);
      $('aiPrompt').value = config.promptTemplate || '';
      $('aiPromptVersion').textContent = '版本 v' + String(config.promptVersion || 1);
      $('aiSettingsError').textContent = '';
    }
    $('lastLoaded').textContent = new Date().toLocaleTimeString();
    $('pageMessage').textContent = '';
  } catch (error) {
    $('pageMessage').textContent = error.message;
    if (error.status === 401) logout(false);
  } finally {
    setBusy(false);
  }
}

async function saveWechatSettings(event) {
  event.preventDefault();
  if (!state.authenticated || state.busy) return;
  $('wechatSettingsError').textContent = '';
  setBusy(true);
  try {
    const result = await api('/api/admin/system/wechat', {
      method: 'PUT', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        app_id: $('wechatAppId').value.trim(),
        app_secret: $('wechatAppSecret').value
      })
    });
    $('wechatAppSecret').value = '';
    $('wechatKeyStatus').textContent = result.has_app_secret ? '已配置' : '未配置';
    toast('微信小程序配置已保存');
  } catch (error) {
    $('wechatSettingsError').textContent = error.message;
    if (error.status === 401) logout(false);
  } finally {
    setBusy(false);
  }
}

async function saveAiSettings(event) {
  event.preventDefault();
  if (!state.authenticated || state.busy) return;
  const stage = state.aiStage;
  $('aiSettingsError').textContent = '';
  setBusy(true);
  try {
    const result = await api('/api/admin/ai/settings/' + stage, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        endpoint: $('aiEndpoint').value.trim(), model: $('aiModel').value.trim(),
        api_key: $('aiApiKey').value,
        enabled: $('aiEnabled').checked, prompt_template: $('aiPrompt').value.trim()
      })
    });
    $('aiApiKey').value = '';
    $('aiKeyStatus').textContent = result.hasApiKey ? '已配置' : '未配置';
    $('aiPromptVersion').textContent = '版本 v' + String(result.promptVersion);
    toast(stage === 'image' ? 'AI 图片配置已保存' : '3D Worker 配置已保存');
  } catch (error) {
    $('aiSettingsError').textContent = error.message;
    if (error.status === 401) logout(false);
  } finally {
    setBusy(false);
  }
}

function openTab(tab) {
  if (!['works', 'orders', 'shipping', 'review', 'model-review', 'account', ...configurationTabs].includes(tab)) return;
  state.tab = tab;
  state.page = 1;
  const settingsMode = tab === 'account' || configurationTabs.includes(tab);
  $('accountPanel').classList.toggle('hidden', tab !== 'account');
  $('wechatSettingsPanel').classList.toggle('hidden', tab !== 'wechat-settings');
  $('aiSettingsPanel').classList.toggle('hidden', !['image-settings', 'model-settings'].includes(tab));
  $('listToolbar').classList.toggle('hidden', settingsMode);
  $('listResults').classList.toggle('hidden', settingsMode);
  $('listPager').classList.toggle('hidden', settingsMode);
  if (tab === 'image-settings' || tab === 'model-settings') {
    const imageMode = tab === 'image-settings';
    state.aiStage = imageMode ? 'image' : 'model';
    $('aiSettingsTitle').textContent = imageMode ? 'AI 图片生成配置' : 'AI 3D 模型生成配置';
    $('aiSettingsHelp').textContent = imageMode
      ? '上传儿童画后由火山方舟 Seedream 生成一张卡通形象。'
      : '确认卡通形象后提交给支持童印异步 Worker 回调协议的 3D 服务。';
    $('aiAdapterDescription').textContent = imageMode
      ? '当前适配器：Seedream。只支持火山方舟官方图片 API，不可直接填入其他模型聚合平台地址。'
      : '当前适配器：3D Worker。需可公网访问的 HTTPS API，并通过 /api/internal/ai-result 回调。不是任意文生 3D API 均可直接使用。';
    $('aiEndpoint').placeholder = imageMode
      ? 'https://ark.cn-beijing.volces.com/api/v3/images/generations'
      : 'https://你的-worker-域名/api/model';
  }
  if (tab === 'account') $('newAdminUsername').value = state.username;
  $('keyword').value = '';
  const filter = $('statusFilter');
  filter.hidden = ['review', 'model-review'].includes(tab);
  $('reviewNotice').classList.toggle('hidden', tab !== 'review');
  $('modelReviewNotice').classList.toggle('hidden', tab !== 'model-review');
  $('keyword').placeholder = ['review', 'model-review'].includes(tab) ? '搜索作品、任务或服务商编号' :
    tab === 'works' ? '搜索作品编号、状态或备注' : '搜索订单编号、收件人或运单';
  filter.innerHTML = tab === 'works'
    ? '<option value="">全部状态</option>' +
      Object.entries(workStatusLabels).map(([value, label]) => '<option value="' + value + '">' + h(label) + '</option>').join('')
    : '<option value="">全部状态</option>' +
      Object.entries(statusLabels).map(([value, label]) => '<option value="' + value + '">' + h(label) + '</option>').join('');
  $('pageTitle').textContent = tab === 'wechat-settings' ? '微信小程序配置' :
    tab === 'image-settings' ? 'AI 图片生成配置' :
    tab === 'model-settings' ? 'AI 3D 模型配置' :
    tab === 'account' ? '账号设置' :
    tab === 'model-review' ? '3D 模型异常' :
    tab === 'review' ? '超时图片任务' :
    tab === 'works' ? '作品管理' : tab === 'shipping' ? '物流发货' : '全部订单';
  $('pageIntro').textContent = tab === 'wechat-settings' ? '配置小程序 AppID 与加密保存的 AppSecret' :
    tab === 'image-settings' ? '设置图片 API、模型、密钥及提示词' :
    tab === 'model-settings' ? '设置 3D Worker API、密钥与 GLB 提示词' :
    tab === 'account' ? '维护管理员用户名与密码' :
    tab === 'model-review' ? '人工核实 Worker 结果，确认失败或补录实际 GLB 模型' :
    tab === 'review' ? '人工核实结果后，确认失败或补录实际生成图片' :
    tab === 'works' ? '原画 → AI 形象 → GLB 白模，当前为只读核查' :
    tab === 'shipping' ? '制作中、已发货及已完成订单' : '收款后制作 → 核实运单 → 发货';
  document.querySelectorAll('[data-tab]').forEach((node) =>
    node.classList.toggle('active', node.dataset.tab === tab));
  // 切换时重新读取相应业务数据，避免作品页和订单页使用彼此的缓存。
  if (tab !== 'account') reload().catch((error) => toast(error.message));
}

async function requestChange(path, body) {
  if (state.busy) return;
  setBusy(true);
  try {
    await api(path, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    toast('操作成功，正在刷新订单');
  } finally {
    setBusy(false);
  }
  await reload();
}

function openShipment(id) {
  // 所有动态数据先经过 HTML 转义；订单 ID 仅从服务器查询结果取得。
  $('modalContainer').innerHTML = '<div class="modal-back"><form id="shipmentForm" class="modal">' +
    '<div class="card-header"><h2>录入真实物流发货</h2><button type="button" class="ghost" data-close>关闭 ✕</button></div>' +
    '<p class="small muted">订单 ' + h(id) + '。仅制作中订单可发货；请核对快递公司和实际运单号。</p>' +
    '<label for="carrier">快递公司</label><input id="carrier" name="carrier" required placeholder="例如 顺丰速运">' +
    '<label for="carrierCode">快递100公司编码</label><input id="carrierCode" name="carrierCode" required placeholder="例如 shunfeng">' +
    '<label for="tracking">真实运单号</label><input id="tracking" name="tracking" required placeholder="例如 SF12345678901">' +
    '<div class="row" style="margin-top:20px"><button type="submit">确认发货</button></div></form></div>';
  $('shipmentForm').dataset.orderId = id;
  $('carrier').focus();
}

async function handleListClick(event) {
  const button = event.target.closest('button[data-action]');
  if (!button || !event.currentTarget.contains(button) || state.busy) return;
  const id = button.dataset.id;
  if (button.dataset.action === 'model-fail' || button.dataset.action === 'model-complete') {
    openModelReview(id, button.dataset.generation, button.dataset.action);
    return;
  }
  if (button.dataset.action === 'review-fail' || button.dataset.action === 'review-complete') {
    // 不信任浏览器的操作状态；提交后服务端会原子校验任务编号与超时阈值。
    openReview(id, button.dataset.generation, button.dataset.action);
    return;
  }
  if (button.dataset.action === 'shipment') {
    openShipment(id);
    return;
  }
  if (button.dataset.action === 'printing' && window.confirm('已经核对微信收款并确认开始制作？')) {
    try {
      await requestChange('/api/admin/orders/' + encodeURIComponent(id) + '/status', { status: 'printing' });
    } catch (error) {
      toast(error.message);
      if (error.status === 401) logout(false);
    }
  }
}

async function ship(event) {
  if (event.target.id !== 'shipmentForm') return;
  event.preventDefault();
  if (!window.confirm('确认快递单号真实有效，并将订单标记为已发货？')) return;
  const form = event.target;
  const id = form.dataset.orderId;
  const body = {
    carrier: $('carrier').value.trim(),
    carrier_code: $('carrierCode').value.trim(),
    tracking: $('tracking').value.trim()
  };
  try {
    await requestChange('/api/admin/orders/' + encodeURIComponent(id) + '/shipment', body);
    $('modalContainer').replaceChildren();
  } catch (error) {
    toast(error.message);
    if (error.status === 401) logout(false);
  }
}

async function saveAdminAccount(event) {
  event.preventDefault();
  if (!state.authenticated || state.busy) return;
  $('accountError').textContent = '';
  const newUsername = $('newAdminUsername').value.trim();
  const currentPassword = $('currentAdminPassword').value;
  const newPassword = $('newAdminPassword').value;
  if (newPassword !== $('confirmAdminPassword').value) {
    $('accountError').textContent = '两次输入的新密码不一致';
    return;
  }
  if (newUsername === state.username && !newPassword) {
    $('accountError').textContent = '请输入新用户名或新密码';
    return;
  }
  setBusy(true);
  try {
    const payload = { currentPassword };
    if (newUsername !== state.username) payload.newUsername = newUsername;
    if (newPassword) payload.newPassword = newPassword;
    await api('/api/admin/account', {
      method: 'PUT', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    await logout(false);
    toast('账号已更新，请使用新的用户名和密码重新登录');
  } catch (error) {
    $('accountError').textContent = error.message;
    if (error.status === 401) await logout(false);
  } finally {
    setBusy(false);
  }
}

async function resumeSession() {
  try {
    const result = await api('/api/admin/account');
    state.authenticated = true;
    state.username = result.username;
    await refreshCsrf();
    $('adminCurrentUser').textContent = '管理员：' + state.username;
    $('loginWrap').classList.add('hidden');
    $('app').classList.remove('hidden');
    await reload();
  } catch {
    state.authenticated = false;
    $('loginWrap').classList.remove('hidden');
    $('app').classList.add('hidden');
  }
}

document.addEventListener('DOMContentLoaded', () => {
  resumeSession();
  $('loginForm').addEventListener('submit', login);
  $('logout').addEventListener('click', () => logout(true));
  $('accountForm').addEventListener('submit', saveAdminAccount);
  $('wechatSettingsForm').addEventListener('submit', saveWechatSettings);
  $('aiSettingsForm').addEventListener('submit', saveAiSettings);
  $('refresh').addEventListener('click', () => reload().catch((error) => toast(error.message)));
  $('keyword').addEventListener('input', () => { state.page = 1; render(); });
  $('statusFilter').addEventListener('change', () => { state.page = 1; render(); });
  $('previous').addEventListener('click', () => { if (state.page > 1) { state.page--; render(); } });
  $('next').addEventListener('click', () => { state.page++; render(); });
  document.querySelectorAll('[data-tab]').forEach((node) =>
    node.addEventListener('click', () => openTab(node.dataset.tab)));
  $('listResults').addEventListener('click', handleListClick);
  $('modalContainer').addEventListener('submit', ship);
  $('modalContainer').addEventListener('submit', submitReview);
  $('modalContainer').addEventListener('submit', submitModelReview);
  $('modalContainer').addEventListener('click', (event) => {
    if (event.target.matches('[data-close]') || event.target.classList.contains('modal-back'))
      $('modalContainer').replaceChildren();
  });
});
