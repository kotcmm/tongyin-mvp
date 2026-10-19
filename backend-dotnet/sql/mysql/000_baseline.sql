-- 童印 .NET MySQL 8+ 基础业务表。只初始化不存在的表，不覆盖已有作品/订单。
-- 顺序：000_baseline -> 001_system_settings -> 003_identity_admin。
CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
INSERT IGNORE INTO ty_store_lock(id) VALUES (1);
CREATE TABLE IF NOT EXISTS ty_works (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL,
    INDEX ix_ty_works_owner (owner_id, created_at),
    INDEX ix_ty_works_recent (created_at DESC, id DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ty_orders (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL,
    INDEX ix_ty_orders_owner (owner_id, created_at),
    INDEX ix_ty_orders_recent (created_at DESC, id DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ty_addresses (
    id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ty_users (
    id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL,
    INDEX ix_ty_users_recent (created_at DESC, id DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ty_audit (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ty_ai_providers (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ty_ai_prompts (
    id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
    created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
