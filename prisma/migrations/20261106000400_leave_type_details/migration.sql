-- Category-specific request details. Maternity uses expected_birth_on for the
-- expected confinement date; paternity uses it for the expected birth date.
ALTER TABLE requests
 ADD COLUMN expected_birth_on date,
 ADD COLUMN birth_on date,
 ADD COLUMN notified_on date,
 ADD COLUMN training_category text
  CHECK (training_category IN ('Company Required', 'Employee-Requested External'));
