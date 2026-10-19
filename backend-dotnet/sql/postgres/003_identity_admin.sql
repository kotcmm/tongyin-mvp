-- 管理员改用 ASP.NET Core Identity。仅建新表，不改动业务数据和旧凭据。
-- 首次登录后移除 ADMIN_BOOTSTRAP_PASSWORD 环境变量。
CREATE TABLE IF NOT EXISTS ty_admin_identity (
    id VARCHAR(64) PRIMARY KEY CHECK (id='admin'),
    user_name VARCHAR(40) NOT NULL,
    normalized_user_name VARCHAR(40) NOT NULL UNIQUE,
    password_hash TEXT NULL,
    security_stamp VARCHAR(128) NULL,
    concurrency_stamp VARCHAR(128) NULL,
    access_failed_count INTEGER NOT NULL DEFAULT 0,
    lockout_end VARCHAR(64) NULL,
    lockout_enabled INTEGER NOT NULL DEFAULT 1
);
