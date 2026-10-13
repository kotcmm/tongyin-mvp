package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"path/filepath"
	"sort"
	"strings"
	"time"

	_ "github.com/go-sql-driver/mysql"
	_ "github.com/jackc/pgx/v5/stdlib"
)

// SQLStore stores complete Work / Order documents in SQL rows, with searchable
// owner and timestamp columns. Keeping the wire schema independent of the SQL
// dialect makes migrating between PostgreSQL and MySQL straightforward.
// The single transactional lock serializes the existing MVP update closures
// across concurrent server processes. This is deliberately small-scale MVP
// storage; replace closure-based snapshots with targeted SQL operations when
// traffic grows.
type SQLStore struct {
	db      *sql.DB
	dialect string
}

func openRepository(c Config) (Repository, error) {
	switch strings.ToLower(c.DBDriver) {
	case "file":
		if !c.Dev {
			return nil, errors.New("file storage is development-only; configure DB_DRIVER=postgres or mysql")
		}
		return newStore(filepath.Join(c.DataDir, "store.json"))
	case "postgres", "mysql":
		if c.DatabaseURL == "" {
			return nil, fmt.Errorf("DATABASE_URL is required for %s", c.DBDriver)
		}
		return newSQLStore(strings.ToLower(c.DBDriver), c.DatabaseURL)
	default:
		return nil, fmt.Errorf("invalid DB_DRIVER %q (allowed: postgres, mysql, file in DEV_MODE)", c.DBDriver)
	}
}

func newSQLStore(dialect, dsn string) (*SQLStore, error) {
	name := "pgx"
	if dialect == "mysql" {
		name = "mysql"
	} else if dialect != "postgres" {
		return nil, fmt.Errorf("unsupported SQL dialect: %s", dialect)
	}
	db, err := sql.Open(name, dsn)
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(10)
	db.SetMaxIdleConns(5)
	db.SetConnMaxLifetime(30 * time.Minute)
	ctx, cancel := context.WithTimeout(context.Background(), 12*time.Second)
	defer cancel()
	if err := db.PingContext(ctx); err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("%s connection failed: %w", dialect, err)
	}
	s := &SQLStore{db: db, dialect: dialect}
	if err := s.ensureSchema(ctx); err != nil {
		_ = db.Close()
		return nil, err
	}
	return s, nil
}

func (s *SQLStore) Close() error { return s.db.Close() }

// Bind parameters are always used for user-derived values.
func (s *SQLStore) bind(stmt string) string {
	if s.dialect != "postgres" {
		return stmt
	}
	n := 0
	return replaceQuestionMarks(stmt, &n)
}
func replaceQuestionMarks(stmt string, n *int) string {
	var b strings.Builder
	for _, r := range stmt {
		if r == '?' {
			*n = *n + 1
			fmt.Fprintf(&b, "$%d", *n)
		} else {
			b.WriteRune(r)
		}
	}
	return b.String()
}

func (s *SQLStore) ensureSchema(ctx context.Context) error {
	ddl := []string{
		`CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY)`,
		`CREATE TABLE IF NOT EXISTS ty_works (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)`,
		`CREATE TABLE IF NOT EXISTS ty_orders (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)`,
        `CREATE TABLE IF NOT EXISTS ty_addresses (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)`,
 `CREATE TABLE IF NOT EXISTS ty_users (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)`,
 `CREATE TABLE IF NOT EXISTS ty_admins (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)`,
 `CREATE TABLE IF NOT EXISTS ty_audit (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)`,
	}
	if s.dialect == "mysql" {
		ddl = []string{
			`CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY) ENGINE=InnoDB`,
			`CREATE TABLE IF NOT EXISTS ty_works (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL, INDEX ix_ty_works_owner (owner_id, created_at)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4`,
			`CREATE TABLE IF NOT EXISTS ty_orders (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL, INDEX ix_ty_orders_owner (owner_id, created_at)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4`,
            `CREATE TABLE IF NOT EXISTS ty_addresses (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4`,
 `CREATE TABLE IF NOT EXISTS ty_users (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4`,
 `CREATE TABLE IF NOT EXISTS ty_admins (id VARCHAR(128) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4`,
 `CREATE TABLE IF NOT EXISTS ty_audit (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4`,
		}
	} else {
		ddl = append(ddl,
			`CREATE INDEX IF NOT EXISTS ix_ty_works_owner ON ty_works(owner_id, created_at)`,
			`CREATE INDEX IF NOT EXISTS ix_ty_orders_owner ON ty_orders(owner_id, created_at)`)
	}
	for _, statement := range ddl {
		if _, err := s.db.ExecContext(ctx, statement); err != nil {
			return fmt.Errorf("SQL schema initialization: %w", err)
		}
	}
	insert := `INSERT INTO ty_store_lock(id) VALUES (1) ON CONFLICT (id) DO NOTHING`
	if s.dialect == "mysql" {
		insert = `INSERT IGNORE INTO ty_store_lock(id) VALUES (1)`
	}
	if _, err := s.db.ExecContext(ctx, insert); err != nil {
		return fmt.Errorf("initialize lock row: %w", err)
	}
	return nil
}

func (s *SQLStore) readSnapshot(ctx context.Context, tx *sql.Tx) (Data, error) {
	d := Data{Works: map[string]*Work{}, Orders: map[string]*Order{}, Addresses: map[string]*ShippingAddress{}, Users: map[string]*User{}, Admins: map[string]*AdminUser{}, Audit: map[string]*AuditRecord{}}
	rows, err := tx.QueryContext(ctx, `SELECT payload FROM ty_works`)
	if err != nil {
		return d, err
	}
	for rows.Next() {
		var raw string
		if err = rows.Scan(&raw); err != nil {
			break
		}
		var w Work
		if err = json.Unmarshal([]byte(raw), &w); err != nil {
			break
		}
		d.Works[w.ID] = &w
	}
	if err == nil {
		err = rows.Err()
	}
	_ = rows.Close()
	if err != nil {
		return d, err
	}
	rows, err = tx.QueryContext(ctx, `SELECT payload FROM ty_orders`)
	if err != nil {
		return d, err
	}
	for rows.Next() {
		var raw string
		if err = rows.Scan(&raw); err != nil {
			break
		}
		var o Order
		if err = json.Unmarshal([]byte(raw), &o); err != nil {
			break
		}
		d.Orders[o.ID] = &o
	}
	if err == nil {
		err = rows.Err()
	}
    _ = rows.Close()
    if err != nil {return d,err}
    rows,err=tx.QueryContext(ctx,`SELECT payload FROM ty_addresses`)
    if err!=nil{return d,err}
    for rows.Next() {
      var raw string
      if err=rows.Scan(&raw);err!=nil{break}
      var a ShippingAddress
      if err=json.Unmarshal([]byte(raw),&a);err!=nil{break}
      d.Addresses[a.Owner]=&a
    }
    if err==nil {err=rows.Err()}
    _=rows.Close()
    if err!=nil{return d,err}
    // Explicitly decoded JSON maps reuse the same transaction / write lock.
    type entry struct { table string; read func(string) error }
    sections:=[]entry{
     {"ty_users",func(raw string)error{var x User;if e:=json.Unmarshal([]byte(raw),&x);e!=nil{return e};d.Users[x.Owner]=&x;return nil}},
     {"ty_admins",func(raw string)error{var x AdminUser;if e:=json.Unmarshal([]byte(raw),&x);e!=nil{return e};d.Admins[x.Owner]=&x;return nil}},
     {"ty_audit",func(raw string)error{var x AuditRecord;if e:=json.Unmarshal([]byte(raw),&x);e!=nil{return e};d.Audit[x.ID]=&x;return nil}},
    }
    for _,section:=range sections{
     next,err:=tx.QueryContext(ctx,"SELECT payload FROM "+section.table)
     if err!=nil{return d,err}
     for next.Next(){
      var raw string
      if err=next.Scan(&raw);err!=nil{break}
      if err=section.read(raw);err!=nil{break}
     }
     if err==nil{err=next.Err()}
     _=next.Close()
     if err!=nil{return d,err}
    }
    return d,nil
}

func (s *SQLStore) update(fn func(*Data) error) error {
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	var lockID int
	// Serializes write transactions, including two server instances, so
	// create-order and payment transitions cannot race across processes.
	if err := tx.QueryRowContext(ctx, `SELECT id FROM ty_store_lock WHERE id=1 FOR UPDATE`).Scan(&lockID); err != nil {
		return err
	}
	before, err := s.readSnapshot(ctx, tx)
	if err != nil {
		return err
	}
	// Deep copy: callback mutates pointers in-place.
	raw, err := json.Marshal(before)
	if err != nil {
		return err
	}
	var after Data
	if err := json.Unmarshal(raw, &after); err != nil {
		return err
	}
	if err := fn(&after); err != nil {
		return err
	}
	if err := s.persistDiff(ctx, tx, "ty_works", before.Works, after.Works); err != nil {
		return err
	}
	if err := s.persistDiff(ctx, tx, "ty_orders", before.Orders, after.Orders); err != nil {
        return err
    }
    if err:=s.persistDiff(ctx,tx,"ty_addresses",before.Addresses,after.Addresses);err!=nil{return err}
    if err:=s.persistDiff(ctx,tx,"ty_users",before.Users,after.Users);err!=nil{return err}
    if err:=s.persistDiff(ctx,tx,"ty_admins",before.Admins,after.Admins);err!=nil{return err}
    if err:=s.persistDiff(ctx,tx,"ty_audit",before.Audit,after.Audit);err!=nil{return err}
	return tx.Commit()
}

// SQL uses document payloads and minimal index columns for this first release.
func (s *SQLStore) persistDiff(ctx context.Context, tx *sql.Tx, table string, before, after any) error {
	var old, next map[string]json.RawMessage
	ob, _ := json.Marshal(before)
	nb, _ := json.Marshal(after)
	if err := json.Unmarshal(ob, &old); err != nil {
		return err
	}
	if err := json.Unmarshal(nb, &next); err != nil {
		return err
	}
	for id := range old {
		if _, ok := next[id]; !ok {
			if _, err := tx.ExecContext(ctx, s.bind("DELETE FROM "+table+" WHERE id=?"), id); err != nil {
				return err
			}
		}
	}
	for id, payload := range next {
		if string(old[id]) == string(payload) {
			continue
		}
		var meta struct {
			Owner     string `json:"owner"`
			CreatedAt string `json:"created_at"`
		}
		if err := json.Unmarshal(payload, &meta); err != nil {
			return err
		}
		if _, found := old[id]; !found {
			_, err := tx.ExecContext(ctx, s.bind("INSERT INTO "+table+"(id, owner_id, created_at, payload) VALUES(?, ?, ?, ?)"), id, meta.Owner, meta.CreatedAt, string(payload))
			if err != nil {
				return err
			}
		} else {
			_, err := tx.ExecContext(ctx, s.bind("UPDATE "+table+" SET owner_id=?, created_at=?, payload=? WHERE id=?"), meta.Owner, meta.CreatedAt, string(payload), id)
			if err != nil {
				return err
			}
		}
	}
	return nil
}

func (s *SQLStore) work(id string) (*Work, bool) {
	var raw string
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	err := s.db.QueryRowContext(ctx, s.bind(`SELECT payload FROM ty_works WHERE id=?`), id).Scan(&raw)
	if err == sql.ErrNoRows {
		return nil, false
	}
	if err != nil {
		log.Printf("SQL read work failed: %v", err)
		return nil, false
	}
	var w Work
	if err := json.Unmarshal([]byte(raw), &w); err != nil {
		log.Printf("SQL malformed work: %v", err)
		return nil, false
	}
	return &w, true
}
func (s *SQLStore) order(id string) (*Order, bool) {
	var raw string
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	err := s.db.QueryRowContext(ctx, s.bind(`SELECT payload FROM ty_orders WHERE id=?`), id).Scan(&raw)
	if err == sql.ErrNoRows {
		return nil, false
	}
	if err != nil {
		log.Printf("SQL read order failed: %v", err)
		return nil, false
	}
	var o Order
	if err := json.Unmarshal([]byte(raw), &o); err != nil {
		log.Printf("SQL malformed order: %v", err)
		return nil, false
	}
	return &o, true
}
func (s *SQLStore) works(owner string, all bool) []*Work {
	out := []*Work{}
	for _, raw := range s.list("ty_works", owner, all) {
		var v Work
		if err := json.Unmarshal([]byte(raw), &v); err == nil {
			out = append(out, &v)
		} else {
			log.Printf("malformed work: %v", err)
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].CreatedAt > out[j].CreatedAt })
	return out
}
func (s *SQLStore) orders(owner string, all bool) []*Order {
	out := []*Order{}
	for _, raw := range s.list("ty_orders", owner, all) {
		var v Order
		if err := json.Unmarshal([]byte(raw), &v); err == nil {
			out = append(out, &v)
		} else {
			log.Printf("malformed order: %v", err)
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].CreatedAt > out[j].CreatedAt })
	return out
}
func (s *SQLStore) list(table, owner string, all bool) []string {
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	query := "SELECT payload FROM " + table
	var args []any
	if !all {
		query += " WHERE owner_id=?"
		args = append(args, owner)
	}
	query += " ORDER BY created_at DESC"
	rows, err := s.db.QueryContext(ctx, s.bind(query), args...)
	if err != nil {
		log.Printf("SQL list failed: %v", err)
		return nil
	}
	defer rows.Close()
	result := []string{}
	for rows.Next() {
		var raw string
		if err := rows.Scan(&raw); err != nil {
			log.Printf("SQL scan failed: %v", err)
			return nil
		}
		result = append(result, raw)
	}
	if err := rows.Err(); err != nil {
		log.Printf("SQL rows failed: %v", err)
	}
	return result
}

func (s *SQLStore) address(owner string) (*ShippingAddress, bool) {
 ctx,cancel:=context.WithTimeout(context.Background(),5*time.Second)
 defer cancel()
 var raw string
 err:=s.db.QueryRowContext(ctx,s.bind(`SELECT payload FROM ty_addresses WHERE id=? AND owner_id=?`),owner,owner).Scan(&raw)
 if err==sql.ErrNoRows{return nil,false}
 if err!=nil {log.Printf("SQL read address failed: %v",err);return nil,false}
 var a ShippingAddress
 if err=json.Unmarshal([]byte(raw),&a);err!=nil{return nil,false}
 if a.Owner!=owner {return nil,false}
 return &a,true
}

func (s *SQLStore) user(owner string) (*User,bool){
 var raw string
 ctx,cancel:=context.WithTimeout(context.Background(),5*time.Second);defer cancel()
 err:=s.db.QueryRowContext(ctx,s.bind("SELECT payload FROM ty_users WHERE id=?"),owner).Scan(&raw)
 if err!=nil{return nil,false}
 var x User
 if json.Unmarshal([]byte(raw),&x)!=nil{return nil,false}
 return &x,true
}
func (s *SQLStore) adminUser(name string) (*AdminUser,bool){
 var raw string
 ctx,cancel:=context.WithTimeout(context.Background(),5*time.Second);defer cancel()
 err:=s.db.QueryRowContext(ctx,s.bind("SELECT payload FROM ty_admins WHERE id=?"),name).Scan(&raw)
 if err!=nil{return nil,false}
 var x AdminUser
 if json.Unmarshal([]byte(raw),&x)!=nil{return nil,false}
 return &x,true
}
