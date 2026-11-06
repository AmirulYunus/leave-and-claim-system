CREATE TABLE attachments (
 id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 request_id integer NOT NULL REFERENCES requests(id),
 file_name text NOT NULL CHECK (length(file_name) BETWEEN 1 AND 200),
 content_type text NOT NULL
  CHECK (content_type IN ('application/pdf', 'image/png', 'image/jpeg')),
 size_bytes integer NOT NULL CHECK (size_bytes > 0 AND size_bytes <= 10485760),
 storage_key text NOT NULL UNIQUE,
 uploaded_by integer NOT NULL REFERENCES employees(id),
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX attachments_request ON attachments(request_id);
