CREATE TABLE IF NOT EXISTS customer_receivables (
    id TEXT NOT NULL PRIMARY KEY,
    customer_name TEXT NOT NULL UNIQUE,
    receivable_amount REAL NOT NULL,
    paid_amount REAL NOT NULL,
    due_date TEXT NULL,
    note TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_customer_receivables_customer_name ON customer_receivables(customer_name);
