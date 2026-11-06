ALTER TABLE requests
 ADD COLUMN registered_on date,
 ADD COLUMN first_marriage boolean,
 ADD COLUMN relationship text CHECK (relationship IN (
  'Spouse', 'Child', 'Natural parent', 'Sibling', 'Grandparent',
  'Parent-in-law', 'Legal guardian'
 ));
