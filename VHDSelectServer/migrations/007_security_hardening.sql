-- 007: Security hardening indexes and documentation
-- Part of deployment security audit fixes

-- Index for efficient token cleanup queries (expired tokens that haven't been used)
CREATE INDEX IF NOT EXISTS idx_deployment_tokens_expires_at
    ON deployment_tokens (expires_at) WHERE used_at IS NULL;

-- Document valid state transitions for deployment tasks
COMMENT ON COLUMN deployment_tasks.status IS
    'Valid transitions: pending→downloading→running→success/failed';
