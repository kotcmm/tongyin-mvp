-- 本迁移按版本执行一次；已提交任务继续使用原混元地址，不重新提交。
ALTER TABLE ty_model_jobs
    ADD COLUMN adapter VARCHAR(64) NOT NULL DEFAULT 'hunyuan3d',
    ADD COLUMN endpoint VARCHAR(1000) NOT NULL DEFAULT 'https://api.ai3d.cloud.tencent.com/v1/ai3d/submit',
    MODIFY COLUMN model VARCHAR(128) NULL;
