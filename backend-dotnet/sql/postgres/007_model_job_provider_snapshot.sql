-- 保存 3D 已提交任务的协议与地址快照，后台修改配置不影响已有 JobId 轮询。
-- 既有 WAIT/RUN 混元任务按之前唯一使用的官方地址填充，不会重新提交。
ALTER TABLE ty_model_jobs
    ADD COLUMN IF NOT EXISTS adapter VARCHAR(64) NOT NULL DEFAULT 'hunyuan3d',
    ADD COLUMN IF NOT EXISTS endpoint VARCHAR(1000) NOT NULL DEFAULT 'https://api.ai3d.cloud.tencent.com/v1/ai3d/submit';
ALTER TABLE ty_model_jobs ALTER COLUMN model TYPE VARCHAR(128);
