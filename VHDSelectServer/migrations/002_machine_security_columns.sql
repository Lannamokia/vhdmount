ALTER TABLE machines ADD COLUMN IF NOT EXISTS evhd_password TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_id VARCHAR(128);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_type VARCHAR(32);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS pubkey_pem TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved BOOLEAN DEFAULT FALSE;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved_at TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked BOOLEAN DEFAULT FALSE;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS last_seen TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS registration_cert_fingerprint VARCHAR(128);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS registration_cert_subject TEXT;

CREATE INDEX IF NOT EXISTS idx_machines_key_id ON machines(key_id);
CREATE INDEX IF NOT EXISTS idx_machines_last_seen ON machines(last_seen);
CREATE INDEX IF NOT EXISTS idx_machines_cert_fingerprint ON machines(registration_cert_fingerprint);