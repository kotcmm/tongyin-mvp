-- 作品删除后的文件清理任务队列：先提交删除和待清理任务，再由后台逐个检查引用并删除磁盘文件。
CREATE TABLE IF NOT EXISTS ty_media_cleanup (
    file_name VARCHAR(100) PRIMARY KEY,
    requested_at VARCHAR(40) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
