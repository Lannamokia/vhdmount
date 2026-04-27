-- 为部署下载令牌增加 AES 加密参数存储
-- 用于服务端动态 AES-CTR 加密、机端解密

ALTER TABLE deployment_tokens
    ADD COLUMN IF NOT EXISTS aes_key VARCHAR(64),
    ADD COLUMN IF NOT EXISTS aes_iv  VARCHAR(64);
