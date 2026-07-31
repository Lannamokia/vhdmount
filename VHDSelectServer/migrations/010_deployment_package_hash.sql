-- 为 deployment_packages 添加 file_hash 列，缓存部署包 SHA256
-- 避免每次机台 poll pending 任务时全量读取文件计算 hash

ALTER TABLE deployment_packages
    ADD COLUMN IF NOT EXISTS file_hash VARCHAR(64);
