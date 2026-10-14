// AI administration: only owner sees this tab. API credentials are never re-displayed.
async function renderAIConfig() {
  const data = await api('/api/admin/ai/config')
  if (state.tab !== 'ai') return
  state.aiProviders = data.providers || []
  state.aiPrompts = data.prompts || {}
  const rows = state.aiProviders.map(p =>
    '<tr><td><strong>' + h(p.name) + '</strong><div class="small muted">' + h(p.adapter) + '</div></td>' +
    '<td>' + (p.stage === 'image' ? '图片' : '3D') + '</td>' +
    '<td>' + h(p.model || 'Worker 自定义') + '</td>' +
    '<td>' + (p.enabled ? '已启用' : '已停用') + (p.default ? ' · 默认' : '') + '</td>' +
    '<td>' + (p.has_key ? '已设置' : '未设置') + '</td>' +
    '<td><button class="ghost" onclick="editAIProvider(\'' + h(p.id) + '\')">编辑</button> ' +
    '<button class="danger" onclick="deleteAIProvider(\'' + h(p.id) + '\')">删除</button></td></tr>'
  ).join('')
  $('body').innerHTML =
    '<div class="panel"><div class="card-header"><h3>模型提供商</h3><button onclick="editAIProvider()">+ 添加提供商</button></div>' +
    '<p class="muted">图片和 3D 独立选择默认提供商。Seedream 直连图片生成；Worker 通过回调提交图片/3D 结果。未选择默认提供商的阶段保留人工上传。</p>' +
    '<div class="table-wrap"><table class="table"><tr><th>名称 / 适配器</th><th>能力</th><th>模型 ID</th><th>状态</th><th>密钥</th><th>操作</th></tr>' +
    (rows || '<tr><td colspan="6" class="muted">尚无提供商，请先添加 Seedream 图片模型。</td></tr>') +
    '</table></div><div class="note">API Key 加密保存在服务端，编辑时留空表示保持原密钥。切换 SESSION_SECRET 将导致旧密钥无法解密。连接真实 AI 接口可能产生费用。</div></div>' +
    '<div class="panel" style="margin-top:18px"><h3>图片生成提示词</h3>' +
    '<p class="muted small">基于儿童原画转卡通图；保存后新任务生效，不修改正在处理的任务。</p>' +
    '<textarea id="aiPromptImage" rows="6" style="width:100%;min-height:150px">' + h(state.aiPrompts.image?.template || '') + '</textarea>' +
    '<div class="row"><span class="muted small">版本 v' + h(state.aiPrompts.image?.version || 1) + '</span><button onclick="saveAIPrompt(\'image\')">保存图片提示词</button></div></div>' +
    '<div class="panel" style="margin-top:18px"><h3>3D 模型生成提示词</h3>' +
    '<p class="muted small">用于 GLB 生成阶段，由相应 Worker 接收。</p>' +
    '<textarea id="aiPromptModel" rows="6" style="width:100%;min-height:150px">' + h(state.aiPrompts.model?.template || '') + '</textarea>' +
    '<div class="row"><span class="muted small">版本 v' + h(state.aiPrompts.model?.version || 1) + '</span><button onclick="saveAIPrompt(\'model\')">保存 3D 提示词</button></div></div>'
}
function editAIProvider(id) {
 const p=state.aiProviders.find(x=>x.id===id) || {name:'',stage:'image',adapter:'seedream',endpoint:'https://ark.cn-beijing.volces.com/api/v3/images/generations',model:'',enabled:false,default:false,has_key:false}
 showModal('<div class="card-header"><h2>'+(id?'编辑提供商':'新增提供商')+'</h2><button class="ghost" onclick="closeModal()">关闭 ✕</button></div>' +
  '<label>显示名称</label><input id="aiName" value="'+h(p.name)+'" placeholder="例如 Seedream 主模型">' +
  '<label>业务能力</label><select id="aiStage" '+(id?'disabled':'')+'><option value="image" '+(p.stage==='image'?'selected':'')+'>图片生成</option><option value="model" '+(p.stage==='model'?'selected':'')+'>3D 生成</option></select>' +
  '<label>接入方式</label><select id="aiAdapter"><option value="seedream" '+(p.adapter==='seedream'?'selected':'')+'>Seedream（直连）</option><option value="worker" '+(p.adapter==='worker'?'selected':'')+'>自定义异步 Worker</option></select>' +
  '<label>API 地址</label><input id="aiEndpoint" value="'+h(p.endpoint)+'" placeholder="https://...">' +
  '<label>模型 ID</label><input id="aiModel" value="'+h(p.model)+'" placeholder="模型 ID；Worker 可留空">' +
  '<label>API Key / Worker Token '+(p.has_key?'（已配置，留空不修改）':'')+'</label><input type="password" autocomplete="new-password" id="aiKey" placeholder="仅服务器存储">' +
  '<div class="row" style="margin:16px 0"><label><input id="aiEnabled" type="checkbox" style="width:auto" '+(p.enabled?'checked':'')+'> 启用</label>' +
  '<label><input id="aiDefault" type="checkbox" style="width:auto" '+(p.default?'checked':'')+'> 设为此能力默认模型</label></div>' +
  '<div class="note small">Seedream 使用官方方舟 API 地址；3D 需要实际部署支持回调的 Worker，单填模型 ID 不会自动完成 3D。</div>' +
  '<div class="row" style="margin-top:20px"><button onclick="saveAIProvider(\''+(id||'')+'\')">保存配置</button></div>')
}
async function saveAIProvider(id) {
 const data={id:id||'',name:$('aiName').value,stage:$('aiStage').value,adapter:$('aiAdapter').value,endpoint:$('aiEndpoint').value,model:$('aiModel').value,api_key:$('aiKey').value,enabled:$('aiEnabled').checked,default:$('aiDefault').checked}
 try {await jsonPost('/api/admin/ai/providers',data);closeModal();toast('配置已保存');await renderAIConfig()}catch(e){toast(e.message)}
}
async function deleteAIProvider(id) {
 if(!confirm('确定删除这个提供商？这不会清理已生成的作品。'))return
 try {await api('/api/admin/ai/providers/'+encodeURIComponent(id),{method:'DELETE'});toast('提供商已删除');await renderAIConfig()}catch(e){toast(e.message)}
}
async function saveAIPrompt(stage) {
 const value=$(stage==='image'?'aiPromptImage':'aiPromptModel').value
 try {await jsonPost('/api/admin/ai/prompts/'+stage,{template:value},'PUT');toast('提示词已保存，新任务立即生效');await renderAIConfig()}catch(e){toast(e.message)}
}
