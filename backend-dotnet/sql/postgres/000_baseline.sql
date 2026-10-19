-- 童印 .NET PostgreSQL 基础业务表；不会删除现有数据。
-- 顺序：000_baseline -> 001_system_settings -> 003_identity_admin。
-- 历史 Go 的 ty_admins 不再创建；旧数据须先备份，再单独运行 900 清理脚本。
BEGIN;
CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY);
INSERT INTO ty_store_lock(id) VALUES (1) ON CONFLICT (id) DO NOTHING;
CREATE TABLE IF NOT EXISTS ty_works (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ty_orders (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ty_addresses (
    id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ty_users (
    id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ty_audit (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ty_ai_providers (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ty_ai_prompts (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ty_works_owner ON ty_works(owner_id, created_at);
CREATE INDEX IF NOT EXISTS ix_ty_orders_owner ON ty_orders(owner_id, created_at);
CREATE INDEX IF NOT EXISTS ix_ty_works_recent ON ty_works(created_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS ix_ty_orders_recent ON ty_orders(created_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS ix_ty_users_recent ON ty_users(created_at DESC, id DESC);
COMMIT;
