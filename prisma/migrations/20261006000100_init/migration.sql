CREATE TABLE employees (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 name text NOT NULL, email text NOT NULL UNIQUE, join_date date NOT NULL,
 role text NOT NULL CHECK(role IN ('Employee','HR admin'))
);
CREATE TABLE requests (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 employee_id integer NOT NULL REFERENCES employees(id),
 kind text NOT NULL CHECK(kind IN ('Leave','Claim')),
 category text NOT NULL, detail text NOT NULL, note text NOT NULL,
 status text NOT NULL DEFAULT 'Pending' CHECK(status IN ('Pending','Approved','Rejected')),
 created_at timestamptz NOT NULL DEFAULT now(),
 reviewed_by integer REFERENCES employees(id), reviewed_at timestamptz
);
CREATE TABLE leave_entries (
 request_id integer NOT NULL REFERENCES requests(id),
 date date NOT NULL, hours numeric NOT NULL CHECK(hours > 0 AND hours <= 8),
 PRIMARY KEY(request_id,date)
);
CREATE TABLE claims (
 request_id integer PRIMARY KEY REFERENCES requests(id),
 amount numeric(12,2) NOT NULL CHECK(amount > 0 AND amount <= 100000)
);
CREATE TABLE timesheet_entries (
 employee_id integer NOT NULL REFERENCES employees(id),
 date date NOT NULL, hours numeric NOT NULL CHECK(hours >= 0 AND hours <= 24),
 updated_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(employee_id,date)
);
CREATE INDEX requests_employee_status ON requests(employee_id,status);
CREATE INDEX leave_entries_date ON leave_entries(date);
