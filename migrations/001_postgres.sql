-- PostgreSQL 14+; optional manual schema deployment (Go initializes automatically).
CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY);
INSERT INTO ty_store_lock(id) VALUES (1) ON CONFLICT (id) DO NOTHING;
CREATE TABLE IF NOT EXISTS ty_works (
  id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
  created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ty_works_owner ON ty_works(owner_id, created_at);
CREATE TABLE IF NOT EXISTS ty_orders (
  id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
  created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ty_orders_owner ON ty_orders(owner_id, created_at);

CREATE TABLE IF NOT EXISTS ty_addresses (
  id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL,
  created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL
);
 
CREATE TABLE IF NOT EXISTS ty_users (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS ty_admins (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS ty_audit (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_ty_users_owner ON ty_users(owner_id, created_at);
CREATE INDEX IF NOT EXISTS ix_ty_admins_owner ON ty_admins(owner_id, created_at);

-- Admin search: recent-page and status filters; SQL LIMIT/OFFSET is always applied.
CREATE INDEX IF NOT EXISTS ix_ty_works_recent ON ty_works(created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_ty_orders_recent ON ty_orders(created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_ty_users_recent ON ty_users(created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_ty_works_search_status ON ty_works ((payload::jsonb->>'status'),created_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS ix_ty_orders_search_status ON ty_orders ((payload::jsonb->>'status'),created_at DESC,id DESC);
