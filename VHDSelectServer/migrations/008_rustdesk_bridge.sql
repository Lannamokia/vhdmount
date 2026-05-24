-- 008: RustDesk Bridge Host feature
-- 详见 .kiro/specs/rustdesk-bridge-host/design.md §"Components and Interfaces" SQL 段落。
-- 全部 DDL idempotent（IF NOT EXISTS / DO NOTHING）。

-- §15.2.1 可信 RustDesk 主控端列表
CREATE TABLE IF NOT EXISTS trusted_rustdesk_controllers (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    controller_id          TEXT        NOT NULL,
    controller_hwid_hash   TEXT,                 -- NULL 表示不要求 hwid 维度精确匹配
    label                  TEXT,
    scope                  TEXT        NOT NULL,
    enabled                BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at             TIMESTAMPTZ,          -- NULL = 永不过期
    audit_note             TEXT,
    CONSTRAINT trusted_rustdesk_controllers_scope_chk
        CHECK (scope = 'global' OR scope LIKE 'machine:%'),
    CONSTRAINT trusted_rustdesk_controllers_hwid_chk
        CHECK (controller_hwid_hash IS NULL OR controller_hwid_hash ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS trusted_rustdesk_controllers_uniq
    ON trusted_rustdesk_controllers (controller_id, COALESCE(controller_hwid_hash, ''), scope);

-- 决策点 6：内存 watermark 的持久化对应表（singleton）
CREATE TABLE IF NOT EXISTS trusted_rustdesk_controllers_watermark (
    singleton              BOOLEAN PRIMARY KEY DEFAULT TRUE,
    snapshot_version       BIGINT NOT NULL DEFAULT 0,
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT trusted_rustdesk_controllers_watermark_singleton_chk CHECK (singleton)
);
INSERT INTO trusted_rustdesk_controllers_watermark (singleton) VALUES (TRUE)
    ON CONFLICT DO NOTHING;

-- §13 RustDeskClientSharedSecret（共享 HMAC 密钥）
CREATE TABLE IF NOT EXISTS rustdesk_bridge_secrets (
    secret_version         BIGINT      PRIMARY KEY,
    key_material           BYTEA       NOT NULL,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id     TEXT,
    activated_at           TIMESTAMPTZ,
    audit_note             TEXT,
    CONSTRAINT rustdesk_bridge_secrets_version_chk
        CHECK (secret_version >= 0 AND secret_version <= 4294967295),
    CONSTRAINT rustdesk_bridge_secrets_material_chk
        CHECK (octet_length(key_material) = 32)
);
CREATE UNIQUE INDEX IF NOT EXISTS rustdesk_bridge_secrets_active_uniq
    ON rustdesk_bridge_secrets ((activated_at IS NOT NULL))
    WHERE activated_at IS NOT NULL;

-- §15.2.2 Password_Wrap_Key 服务端持久化（LRU-2）
CREATE TABLE IF NOT EXISTS rustdesk_wrap_keys (
    wrap_key_id            TEXT        PRIMARY KEY,
    machine_id             TEXT        NOT NULL,
    key_material           BYTEA       NOT NULL,
    issued_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at             TIMESTAMPTZ NOT NULL,
    CONSTRAINT rustdesk_wrap_keys_id_format_chk CHECK (wrap_key_id ~ '^[0-9a-f]{32}$'),
    CONSTRAINT rustdesk_wrap_keys_material_chk CHECK (octet_length(key_material) = 32),
    CONSTRAINT rustdesk_wrap_keys_machine_fk
        FOREIGN KEY (machine_id) REFERENCES machines(machine_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS rustdesk_wrap_keys_machine_idx
    ON rustdesk_wrap_keys (machine_id, issued_at DESC);

-- §15.9 Report 上报记录
CREATE TABLE IF NOT EXISTS rustdesk_reports (
    machine_id             TEXT        PRIMARY KEY REFERENCES machines(machine_id) ON DELETE CASCADE,
    rust_desk_id           TEXT        NOT NULL,
    password_kind          TEXT        NOT NULL,
    password_plaintext     TEXT,
    password_hash_prefix   TEXT,
    last_wrap_key_id       TEXT,
    secret_version         BIGINT,
    reported_at            TIMESTAMPTZ NOT NULL,
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT rustdesk_reports_password_kind_chk
        CHECK (password_kind IN ('temporary', 'permanent', 'preset', 'absent'))
);

-- 决策点 7：Bridge_Policy_Signing_Pubkey 持久化
CREATE TABLE IF NOT EXISTS bridge_policy_signing_keys (
    key_id                 TEXT        PRIMARY KEY,
    public_key_pem         TEXT        NOT NULL,
    private_key_pem        TEXT        NOT NULL,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    activated_at           TIMESTAMPTZ,
    CONSTRAINT bridge_policy_signing_keys_pem_chk
        CHECK (public_key_pem LIKE '-----BEGIN PUBLIC KEY-----%')
);
CREATE UNIQUE INDEX IF NOT EXISTS bridge_policy_signing_keys_active_uniq
    ON bridge_policy_signing_keys ((activated_at IS NOT NULL))
    WHERE activated_at IS NOT NULL;
