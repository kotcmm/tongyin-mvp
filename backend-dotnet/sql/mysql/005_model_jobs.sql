-- 混元 3D 异步任务：保存真实 JobId，支持重启恢复而不重复收费提交。
CREATE TABLE IF NOT EXISTS ty_model_jobs (
    generation_id VARCHAR(64) PRIMARY KEY,
    work_id VARCHAR(64) NOT NULL,
    owner_id VARCHAR(128) NOT NULL,
    provider_id VARCHAR(64) NOT NULL,
    prompt_version INTEGER NOT NULL,
    provider_job_id VARCHAR(128) NULL,
    api_key_cipher TEXT NULL,
    model VARCHAR(32) NULL,
    status VARCHAR(16) NOT NULL,
    created_at VARCHAR(40) NOT NULL,
    updated_at VARCHAR(40) NOT NULL,
    INDEX ix_ty_model_jobs_status(status,updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
