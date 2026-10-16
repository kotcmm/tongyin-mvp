-- 微信小程序应用凭据的加密存储，使用独立业务配置表。
-- 必须在部署新版本 API 前执行；不修改既有数据。
CREATE TABLE IF NOT EXISTS ty_system_settings (
    id VARCHAR(64) PRIMARY KEY,
    payload TEXT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
