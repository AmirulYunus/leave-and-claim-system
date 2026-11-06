ALTER TABLE requests DROP CONSTRAINT requests_status_check;
ALTER TABLE requests ADD CONSTRAINT requests_status_check
 CHECK (status IN ('Pending', 'Approved', 'Rejected', 'Cancelled'));
-- A Cancellation is requested by the employee for future approved leave; a
-- Correction is HR cancelling approved Emergency leave that has started.
CREATE TABLE cancellations (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 request_id integer NOT NULL REFERENCES requests(id),
 kind text NOT NULL CHECK (kind IN ('Cancellation', 'Correction')),
 reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 500),
 status text NOT NULL DEFAULT 'Pending'
  CHECK (status IN ('Pending', 'Approved', 'Rejected')),
 requested_by integer NOT NULL REFERENCES employees(id),
 created_at timestamptz NOT NULL DEFAULT now(),
 reviewed_by integer REFERENCES employees(id),
 reviewed_at timestamptz
);
CREATE INDEX cancellations_request ON cancellations(request_id);
