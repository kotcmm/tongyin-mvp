-- 童印 PostgreSQL 旧管理员数据库表清理。
-- 仅删除废弃的 Go ty_admins 和自写密码系统 ty_admin_credentials。
-- 不删除 ty_admin_identity、ty_users、ty_works、ty_orders、ty_store_lock 或审计表。
-- 注意：DROP TABLE 会永久清除旧账号及其数据，请先做好独立数据库备份。
--
-- 建议执行前在数据库客户端查看：
-- SELECT table_name FROM information_schema.tables
--   WHERE table_schema='public' AND table_name LIKE 'ty_%' ORDER BY table_name;
-- SELECT COUNT(*) FROM public.ty_admin_identity;  -- 至少应有 1 条新管理员数据
--
-- 如果你还没有用 ASP.NET Core Identity 完成首次管理员登录，本脚本会拒绝执行。
-- 本脚本不使用 CASCADE；若存在外键依赖，会失败并完整回滚。
BEGIN;
DO $$
BEGIN
    IF to_regclass('public.ty_admin_identity') IS NULL THEN
        RAISE EXCEPTION '安全检查失败：ty_admin_identity 尚不存在，请先执行 003_identity_admin.sql';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.ty_admin_identity WHERE id = 'admin') THEN
        RAISE EXCEPTION '安全检查失败：请先使用 Identity 用户名和密码成功初始化管理员，再清理旧表';
    END IF;
END
$$;
DROP TABLE IF EXISTS public.ty_admins, public.ty_admin_credentials RESTRICT;
COMMIT;
