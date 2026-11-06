ALTER TABLE employees
 ADD COLUMN password_hash text,
 ADD COLUMN must_change_password boolean NOT NULL DEFAULT true;
-- Only a SHA-256 digest of each session token is stored.
CREATE TABLE sessions (
 token_hash text PRIMARY KEY,
 employee_id integer NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
 created_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz NOT NULL
);
CREATE INDEX sessions_employee ON sessions(employee_id);
