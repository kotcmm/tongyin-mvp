-- 童印改用 ASP.NET Core Data Protection；旧密文不再可解密。
-- 仅清理 AI 服务商配置；执行前先备份，随后在管理后台重新录入 API Key。
START TRANSACTION;
DELETE FROM ty_ai_providers;
COMMIT;
