-- 童印改用 ASP.NET Core Data Protection；旧 Go 兼容密文不再可解密。
-- 此脚本只删除旧 AI 服务商及其加密 API Key 配置，不会删除作品、订单、
-- 微信账号、图片、GLB、提示词或已经登记的模型任务。
-- 请先备份数据库，再执行；随后在管理后台重新填写 Seedream 与混元 API Key。
BEGIN;
DELETE FROM ty_ai_providers;
COMMIT;
