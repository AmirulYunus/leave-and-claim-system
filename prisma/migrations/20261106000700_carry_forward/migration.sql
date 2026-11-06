-- Hours of an approved request funded by carry-forward, fixed at approval so a
-- cancellation restores exactly the buckets it used.
ALTER TABLE requests
 ADD COLUMN carry_forward_hours numeric NOT NULL DEFAULT 0
  CHECK (carry_forward_hours >= 0);
-- A written higher limit, recorded by HR from 1 October to 15 December.
CREATE TABLE carry_forward_exceptions (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 employee_id integer NOT NULL REFERENCES employees(id),
 source_year integer NOT NULL,
 limit_hours integer NOT NULL
  CHECK (limit_hours > 40 AND limit_hours <= 1000 AND limit_hours % 4 = 0),
 document_reference text NOT NULL
  CHECK (length(btrim(document_reference)) BETWEEN 1 AND 200),
 recorded_by integer NOT NULL REFERENCES employees(id),
 recorded_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE (employee_id, source_year)
);
-- HR's confirmation of a closed year's carry-forward. Once it exists the
-- closed year's Annual Leave is locked.
CREATE TABLE carry_forwards (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 employee_id integer NOT NULL REFERENCES employees(id),
 source_year integer NOT NULL,
 unused_hours integer NOT NULL CHECK (unused_hours >= 0 AND unused_hours % 4 = 0),
 limit_hours integer NOT NULL CHECK (limit_hours >= 40),
 carry_hours integer NOT NULL CHECK (
  carry_hours >= 0 AND carry_hours % 4 = 0
  AND carry_hours <= unused_hours AND carry_hours <= limit_hours
 ),
 reason text CHECK (length(reason) <= 2000),
 document_reference text CHECK (length(document_reference) <= 200),
 confirmed_by integer NOT NULL REFERENCES employees(id),
 confirmed_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE (employee_id, source_year)
);
