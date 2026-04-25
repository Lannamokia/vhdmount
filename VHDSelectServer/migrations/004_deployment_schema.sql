CREATE TABLE IF NOT EXISTS deployment_packages (
    id              SERIAL PRIMARY KEY,
    package_id      VARCHAR(64) UNIQUE NOT NULL,
    name            VARCHAR(256) NOT NULL,
    version         VARCHAR(64) NOT NULL,
    type            VARCHAR(32) NOT NULL CHECK (type IN ('software-deploy', 'file-deploy')),
    signer          VARCHAR(128) NOT NULL,
    file_path       VARCHAR(512) NOT NULL,
    file_size       BIGINT NOT NULL,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    expires_at      TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS deployment_tasks (
    id              SERIAL PRIMARY KEY,
    task_id         VARCHAR(64) UNIQUE NOT NULL,
    package_id      VARCHAR(64) NOT NULL REFERENCES deployment_packages(package_id),
    machine_id      VARCHAR(64) NOT NULL,
    task_type       VARCHAR(32) NOT NULL DEFAULT 'deploy'
                        CHECK (task_type IN ('deploy', 'uninstall')),
    status          VARCHAR(32) NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending', 'downloading', 'running', 'success', 'failed')),
    scheduled_at    TIMESTAMPTZ,
    started_at      TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    error_message   TEXT,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_deployment_tasks_machine_status ON deployment_tasks(machine_id, status);
CREATE INDEX IF NOT EXISTS idx_deployment_tasks_package ON deployment_tasks(package_id);

CREATE TABLE IF NOT EXISTS deployment_tokens (
    id              SERIAL PRIMARY KEY,
    token           VARCHAR(128) UNIQUE NOT NULL,
    task_id         VARCHAR(64) NOT NULL REFERENCES deployment_tasks(task_id),
    machine_id      VARCHAR(64) NOT NULL,
    package_id      VARCHAR(64) NOT NULL,
    resource_type   VARCHAR(32) NOT NULL CHECK (resource_type IN ('package', 'signature')),
    expires_at      TIMESTAMPTZ NOT NULL,
    used_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_deployment_tokens_task ON deployment_tokens(task_id);
CREATE INDEX IF NOT EXISTS idx_deployment_tokens_expires ON deployment_tokens(expires_at);

CREATE TABLE IF NOT EXISTS deployment_records (
    id              SERIAL PRIMARY KEY,
    record_id       VARCHAR(64) UNIQUE NOT NULL,
    machine_id      VARCHAR(64) NOT NULL,
    package_id      VARCHAR(64) NOT NULL,
    name            VARCHAR(256) NOT NULL,
    version         VARCHAR(64) NOT NULL,
    type            VARCHAR(32) NOT NULL CHECK (type IN ('software-deploy', 'file-deploy')),
    target_path     VARCHAR(512),
    status          VARCHAR(32) NOT NULL DEFAULT 'success'
                        CHECK (status IN ('success', 'failed', 'uninstalled')),
    deployed_at     TIMESTAMPTZ NOT NULL,
    uninstalled_at  TIMESTAMPTZ,
    synced_at       TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_deployment_records_machine ON deployment_records(machine_id);
CREATE INDEX IF NOT EXISTS idx_deployment_records_name_version ON deployment_records(name, version);
