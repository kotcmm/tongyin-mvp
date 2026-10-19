-- 作品删除后的文件清理任务队列：先提交删除和待清理任务，再由后台逐个检查引用并删除磁盘文件。
-- 本迁移幂等，不改写或删除历史媒体；上线前执行。
CREATE TABLE IF NOT EXISTS ty_media_cleanup (
    file_name VARCHAR(100) PRIMARY KEY,
    requested_at VARCHAR(40) NOT NULL
);
