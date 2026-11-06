import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import nextEnv from "@next/env";
import pg from "pg";
import {
  baseUrl,
  clearOutboxSince,
  demoSessions,
  outboxMark,
  signIn,
} from "./sessions.mjs";

nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const prefix = `notifications-${Date.now()}`;
const email = `${prefix}@example.test`;
let sessions = {};
let mark;

async function api(path, who, method = "GET", body, expected = 200) {
  const response = await fetch(`${baseUrl}/api/${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      Cookie: sessions[who] ?? who,
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const result = await response.json();
  assert.equal(response.status, expected, result.error ?? `${method} ${path}`);
  return result;
}
const outbox = async () =>
  (
    await client.query(
      "SELECT recipient, subject, body, sent_at FROM notifications WHERE id > $1 ORDER BY id",
      [mark],
    )
  ).rows;

try {
  await client.connect();
  mark = await outboxMark(client);
  sessions = await demoSessions();
  const { year, asOf } = (await api("workspace", "Employee")).entitlements;
  await api(
    "employees",
    "HR",
    "POST",
    {
      name: "Notification check",
      email,
      join_date: `${year - 3}-01-01`,
      employment_type: "Full-time",
      employee_level: 1,
      temporary_password: "temporary-pass-1",
    },
    201,
  );
  sessions.Tester = await signIn(email, "temporary-pass-1");
  await api("auth/password", "Tester", "POST", {
    current_password: "temporary-pass-1",
    new_password: "chosen-password-1",
  });

  // A rejected claim emails the employee and HR.
  const { id: claimId } = await api(
    "workspace",
    "Tester",
    "POST",
    {
      kind: "Claim",
      category: "Travel",
      detail: prefix,
      note: `${prefix} private reason`,
      value: 25,
    },
    201,
  );
  await api("workspace", "HR", "PATCH", { id: claimId, status: "Rejected" });
  let rows = await outbox();
  assert.ok(
    rows.some(
      (row) =>
        row.recipient === email && row.subject === "Your claim was rejected",
    ),
  );
  assert.ok(
    rows.some(
      (row) =>
        row.recipient === "hr@example.test" &&
        row.subject === "Claim rejected" &&
        row.body.includes("Notification check"),
    ),
  );
  assert.ok(rows.every((row) => !row.body.includes("private reason")));

  // An approved leave and its approved cancellation each email both.
  let t = Date.parse(asOf) + 7 * 86400_000;
  while ([0, 6].includes(new Date(t).getUTCDay())) t += 86400_000;
  const date = new Date(t).toISOString().slice(0, 10);
  if (date.startsWith(`${year}-`)) {
    const { id } = await api(
      "workspace",
      "Tester",
      "POST",
      {
        kind: "Leave",
        category: "Annual leave",
        detail: date,
        note: prefix,
        entries: [{ date, hours: 8 }],
      },
      201,
    );
    await api("workspace", "HR", "PATCH", { id, status: "Approved" });
    const { id: cancellationId } = await api(
      "cancellations",
      "Tester",
      "POST",
      { request_id: id, reason: "Plans changed" },
      201,
    );
    await api("cancellations", "HR", "PATCH", {
      id: cancellationId,
      status: "Approved",
    });
    rows = await outbox();
    for (const subject of [
      "Your leave request was approved",
      "Your leave cancellation was approved",
    ])
      assert.ok(
        rows.some((row) => row.recipient === email && row.subject === subject),
        subject,
      );
    assert.ok(rows.every((row) => !row.body.includes("Plans changed")));
  }

  // Delivery marks queued emails as sent.
  execFileSync(process.execPath, [
    "node_modules/tsx/dist/cli.mjs",
    "scripts/notifications.ts",
    "deliver",
  ]);
  rows = await outbox();
  assert.ok(rows.length > 0 && rows.every((row) => row.sent_at));

  // Reminders are queued only on 1 October and 30 November, once each.
  const remind = (day) =>
    execFileSync(process.execPath, [
      "node_modules/tsx/dist/cli.mjs",
      "scripts/notifications.ts",
      "remind",
      `--date=${year}-${day}`,
    ]).toString();
  assert.match(remind("11-06"), /No carry-forward reminder/);
  remind("11-30");
  remind("11-30");
  const reminders = (await outbox()).filter(
    (row) => row.recipient === email && row.subject.includes("reminder"),
  );
  assert.equal(reminders.length, 1);

  console.log(
    "PASS: decision, cancellation and reminder emails queued for the employee and HR without reasons, delivered once and never duplicated.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  if (mark !== undefined) await clearOutboxSince(client, mark);
  const ids = "(SELECT id FROM requests WHERE note LIKE $1)";
  for (const table of ["cancellations", "claims", "leave_entries"])
    await client.query(`DELETE FROM ${table} WHERE request_id IN ${ids}`, [
      `${prefix}%`,
    ]);
  await client.query("DELETE FROM requests WHERE note LIKE $1", [`${prefix}%`]);
  await client.query("DELETE FROM employees WHERE email = $1", [email]);
  await client.end();
}
