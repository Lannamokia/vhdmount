-- 支持游戏内容更新部署类型 game-option-deploy
-- 扩展 deployment_packages 与 deployment_records 的 type 约束

ALTER TABLE deployment_packages
    DROP CONSTRAINT IF EXISTS deployment_packages_type_check;

ALTER TABLE deployment_packages
    ADD CONSTRAINT deployment_packages_type_check
    CHECK (type IN ('software-deploy', 'file-deploy', 'game-option-deploy'));

ALTER TABLE deployment_records
    DROP CONSTRAINT IF EXISTS deployment_records_type_check;

ALTER TABLE deployment_records
    ADD CONSTRAINT deployment_records_type_check
    CHECK (type IN ('software-deploy', 'file-deploy', 'game-option-deploy'));
