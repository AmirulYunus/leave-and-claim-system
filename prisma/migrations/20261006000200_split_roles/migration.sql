BEGIN;
ALTER TABLE employees DROP CONSTRAINT employees_role_check;
UPDATE employees SET role = 'HR' WHERE role = 'HR admin';
ALTER TABLE employees ADD CONSTRAINT employees_role_check
  CHECK (role IN ('Employee', 'HR', 'Admin'));
COMMIT;
