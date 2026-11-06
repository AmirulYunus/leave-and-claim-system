CREATE TABLE departments (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 name text NOT NULL UNIQUE CHECK (length(btrim(name)) BETWEEN 1 AND 100),
 is_active boolean NOT NULL DEFAULT true
);
CREATE TABLE cost_centres (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 name text NOT NULL UNIQUE CHECK (length(btrim(name)) BETWEEN 1 AND 100),
 is_active boolean NOT NULL DEFAULT true
);
ALTER TABLE employees
 ADD COLUMN position text CHECK (length(position) <= 100),
 ADD COLUMN employment_type text
  CHECK (employment_type IN ('Full-time', 'Part-time', 'Intern')),
 ADD COLUMN employee_level smallint CHECK (employee_level BETWEEN 1 AND 4),
 ADD COLUMN probation_completed_on date,
 ADD COLUMN leaving_date date,
 ADD COLUMN is_active boolean NOT NULL DEFAULT true,
 ADD COLUMN department_id integer REFERENCES departments(id),
 ADD COLUMN cost_centre_id integer REFERENCES cost_centres(id),
 ADD CONSTRAINT employees_leaving_date_check CHECK (leaving_date >= join_date);
