ALTER TABLE deployment_tasks
    ADD COLUMN IF NOT EXISTS lease_expires_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_deployment_tasks_machine_status_lease
    ON deployment_tasks(machine_id, status, lease_expires_at);
