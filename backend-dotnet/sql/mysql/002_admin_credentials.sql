-- 单管理员账户密码采用带盐的 PBKDF2-SHA256 哈希，不保存明文；执行一次即可。
CREATE TABLE IF NOT EXISTS ty_admin_credentials (
    id INTEGER PRIMARY KEY,
    username VARCHAR(40) NOT NULL,
    password_salt VARCHAR(128) NOT NULL,
    password_hash VARCHAR(128) NOT NULL,
    session_version VARCHAR(64) NOT NULL,
    CONSTRAINT chk_ty_admin_single_account CHECK (id = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
