-- Outbox of emails. Rows are written in the same transaction as the decision
-- they report and delivered separately by `npm run notifications:deliver`.
CREATE TABLE notifications (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 recipient text NOT NULL CHECK (length(recipient) BETWEEN 3 AND 254),
 subject text NOT NULL CHECK (length(subject) BETWEEN 1 AND 200),
 body text NOT NULL,
 -- Set for scheduled messages so a rerun cannot queue a duplicate.
 dedupe_key text UNIQUE,
 created_at timestamptz NOT NULL DEFAULT now(),
 attempts integer NOT NULL DEFAULT 0,
 sent_at timestamptz,
 last_error text
);
CREATE INDEX notifications_sent_at ON notifications(sent_at);
