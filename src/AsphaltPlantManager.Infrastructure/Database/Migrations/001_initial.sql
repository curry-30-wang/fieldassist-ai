CREATE TABLE IF NOT EXISTS schema_migrations (
    version TEXT NOT NULL PRIMARY KEY,
    applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS records (
    id TEXT NOT NULL PRIMARY KEY,
    template_id TEXT NOT NULL,
    template_version INTEGER NOT NULL,
    period_start TEXT NOT NULL,
    period_end TEXT NOT NULL,
    status INTEGER NOT NULL,
    archive_number TEXT NULL,
    change_note TEXT NULL,
    sealed_at TEXT NULL,
    header_snapshot_json TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    search_text TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS record_versions (
    record_id TEXT NOT NULL,
    version INTEGER NOT NULL,
    archive_number TEXT NULL,
    template_id TEXT NULL,
    template_version INTEGER NULL,
    header_snapshot_json TEXT NULL,
    payload_json TEXT NOT NULL,
    change_note TEXT NULL,
    created_at TEXT NOT NULL,
    PRIMARY KEY (record_id, version),
    FOREIGN KEY (record_id) REFERENCES records(id)
);

CREATE TABLE IF NOT EXISTS master_data (
    category TEXT NOT NULL,
    item_key TEXT NOT NULL,
    value TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (category, item_key)
);

CREATE TABLE IF NOT EXISTS settings (
    setting_key TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS archive_sequences (
    sequence_key TEXT NOT NULL PRIMARY KEY,
    next_value INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS trash (
    record_id TEXT NOT NULL PRIMARY KEY,
    deleted_at TEXT NOT NULL,
    FOREIGN KEY (record_id) REFERENCES records(id)
);

CREATE INDEX IF NOT EXISTS ix_records_period_start ON records(period_start);
CREATE INDEX IF NOT EXISTS ix_records_template_id ON records(template_id);
