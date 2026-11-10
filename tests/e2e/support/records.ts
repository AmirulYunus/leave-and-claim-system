import { expect } from "@playwright/test";

export type Db = { query: (sql: string) => Promise<string[][]> };

export function sqlString(value: string) {
  return `'${value.replace(/'/g, "''")}'`;
}

async function one(db: Db, sql: string, label: string) {
  const rows = await db.query(sql);
  if (rows.length !== 1) {
    throw new Error(`Expected one ${label}, found ${rows.length}.`);
  }
  return rows[0];
}

export async function employeeId(db: Db, email: string) {
  const row = await one(
    db,
    `SELECT id::text FROM employees WHERE email = ${sqlString(email)}`,
    `employee ${email}`,
  );
  return row[0];
}

export async function sessionState(db: Db, email: string) {
  const row = await one(
    db,
    `SELECT count(*)::text,
            count(*) FILTER (WHERE s.expires_at > s.created_at)::text
     FROM sessions s
     JOIN employees e ON e.id = s.employee_id
     WHERE e.email = ${sqlString(email)}`,
    `sessions for ${email}`,
  );
  return { total: Number(row[0]), stillValid: Number(row[1]) };
}

export async function employeeRecord(db: Db, email: string) {
  const rows = await db.query(
    `SELECT role,
            coalesce(employment_type, ''),
            coalesce(employee_level::text, ''),
            to_char(join_date, 'YYYY-MM-DD'),
            CASE WHEN must_change_password THEN 'yes' ELSE 'no' END,
            CASE WHEN is_active THEN 'yes' ELSE 'no' END
     FROM employees WHERE email = ${sqlString(email)}`,
  );
  if (rows.length !== 1) return null;
  const [role, employmentType, level, joinDate, mustChange, active] = rows[0];
  return {
    role,
    employmentType,
    level,
    joinDate,
    mustChange,
    active,
  };
}

export async function employmentInputs(db: Db, email: string) {
  const rows = await db.query(
    `SELECT to_char(join_date, 'YYYY-MM-DD'),
            coalesce(employee_level::text, ''),
            coalesce(employment_type, '')
     FROM employees WHERE email = ${sqlString(email)}`,
  );
  if (!rows[0]) return null;
  return {
    joinDate: rows[0][0],
    level: Number(rows[0][1]),
    employmentType: rows[0][2],
  };
}

export async function usedAnnualHours(db: Db, email: string, year: string) {
  const row = await one(
    db,
    `SELECT coalesce(sum(le.hours), 0)::text
     FROM leave_entries le
     JOIN requests r ON r.id = le.request_id
     JOIN employees e ON e.id = r.employee_id
     WHERE e.email = ${sqlString(email)}
       AND r.status IN ('Pending', 'Approved')
       AND r.category IN ('Annual leave', 'Emergency leave')
       AND le.date >= ${sqlString(`${year}-01-01`)}::date
       AND le.date <= ${sqlString(`${year}-12-31`)}::date`,
    `annual hours for ${email}`,
  );
  return Number(row[0]);
}

export async function takenLeaveDates(db: Db, email: string) {
  const rows = await db.query(
    `SELECT to_char(le.date, 'YYYY-MM-DD')
     FROM leave_entries le
     JOIN requests r ON r.id = le.request_id
     JOIN employees e ON e.id = r.employee_id
     WHERE e.email = ${sqlString(email)}
       AND r.status NOT IN ('Rejected', 'Cancelled')`,
  );
  return new Set(rows.map((row) => row[0]));
}

export type LeaveRecord = {
  status: string;
  reviewerId: string;
  reviewed: boolean;
  kind: string;
  category: string;
  carryHours: number;
  email: string;
};

export async function leaveRequest(db: Db, note: string) {
  const rows = await db.query(
    `SELECT r.status,
            coalesce(r.reviewed_by::text, ''),
            CASE WHEN r.reviewed_at IS NULL THEN 'no' ELSE 'yes' END,
            r.kind,
            r.category,
            r.carry_forward_hours::text,
            e.email
     FROM requests r
     JOIN employees e ON e.id = r.employee_id
     WHERE r.note = ${sqlString(note)}`,
  );
  if (rows.length !== 1) return null;
  const [status, reviewerId, reviewed, kind, category, carry, email] = rows[0];
  return {
    status,
    reviewerId,
    reviewed: reviewed === "yes",
    kind,
    category,
    carryHours: Number(carry),
    email,
  } satisfies LeaveRecord;
}

export async function leaveEntries(db: Db, note: string) {
  const rows = await db.query(
    `SELECT to_char(le.date, 'YYYY-MM-DD'), le.hours::text
     FROM leave_entries le
     JOIN requests r ON r.id = le.request_id
     WHERE r.note = ${sqlString(note)}
     ORDER BY le.date`,
  );
  return rows.map((row) => [row[0], Number(row[1])] as [string, number]);
}

export async function requestCount(db: Db, note: string) {
  const row = await one(
    db,
    `SELECT count(*)::text FROM requests WHERE note = ${sqlString(note)}`,
    `request count for ${note}`,
  );
  return Number(row[0]);
}

export type ClaimRecord = {
  status: string;
  reviewerId: string;
  reviewed: boolean;
  kind: string;
  category: string;
  detail: string;
  amount: number;
  email: string;
};

export async function claimRequest(
  db: Db,
  note: string,
): Promise<ClaimRecord | null> {
  const rows = await db.query(
    `SELECT r.status,
            coalesce(r.reviewed_by::text, ''),
            CASE WHEN r.reviewed_at IS NULL THEN 'no' ELSE 'yes' END,
            r.kind,
            r.category,
            r.detail,
            c.amount::text,
            e.email
     FROM requests r
     JOIN claims c ON c.request_id = r.id
     JOIN employees e ON e.id = r.employee_id
     WHERE r.note = ${sqlString(note)}`,
  );
  if (rows.length !== 1) return null;
  const [status, reviewerId, reviewed, kind, category, detail, amount, email] =
    rows[0];
  return {
    status,
    reviewerId,
    reviewed: reviewed === "yes",
    kind,
    category,
    detail,
    amount: Number(amount),
    email,
  };
}

export async function notificationSubjects(
  db: Db,
  email: string,
  employeeName: string,
) {
  return db.query(
    `SELECT recipient, subject
     FROM notifications
     WHERE recipient = ${sqlString(email)}
        OR body LIKE ${sqlString(`%${employeeName}%`)}
     ORDER BY subject, recipient`,
  );
}

export async function expectDecisionMail(
  db: Db,
  input: {
    email: string;
    name: string;
    employeeSubject: string;
    hrSubject: string;
    hrEmail: string;
    adminEmail: string;
  },
) {
  const mail = await notificationSubjects(db, input.email, input.name);
  expect(mail).toEqual(
    expect.arrayContaining([
      [input.email, input.employeeSubject],
      [input.hrEmail, input.hrSubject],
    ]),
  );
  expect(mail.filter((row) => row[0] === input.adminEmail)).toEqual([]);
}

export async function timesheetHours(db: Db, email: string, date: string) {
  const rows = await db.query(
    `SELECT t.hours::text
     FROM timesheet_entries t
     JOIN employees e ON e.id = t.employee_id
     WHERE e.email = ${sqlString(email)}
       AND t.date = ${sqlString(date)}::date`,
  );
  return rows.map((row) => Number(row[0]));
}

export async function attachmentCount(db: Db, note: string) {
  const row = await one(
    db,
    `SELECT count(*)::text
     FROM attachments a
     JOIN requests r ON r.id = a.request_id
     WHERE r.note = ${sqlString(note)}`,
    `attachments for ${note}`,
  );
  return Number(row[0]);
}
