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
