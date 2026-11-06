import assert from "node:assert/strict";
import { unlink } from "node:fs/promises";
import path from "node:path";
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
const prefix = `cancellations-${Date.now()}`;
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

const day = 86400_000;
const iso = (time) => new Date(time).toISOString().slice(0, 10);
const weekday = (from) => {
  let t = Date.parse(from);
  while ([0, 6].includes(new Date(t).getUTCDay())) t += day;
  return iso(t);
};
const leave = (category, date) => ({
  kind: "Leave",
  category,
  detail: date,
  note: prefix,
  entries: [{ date, hours: 8 }],
});
const statusOf = async (id) =>
  (await api("workspace", "Tester")).requests.find((r) => r.id === id).status;

try {
  await client.connect();
  mark = await outboxMark(client);
  sessions = await demoSessions();
  const { year } = (await api("workspace", "Employee")).entitlements;
  await api(
    "employees",
    "HR",
    "POST",
    {
      name: "Cancellation check",
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
  const { asOf } = (await api("workspace", "Tester")).entitlements;
  const approve = (id) =>
    api("workspace", "HR", "PATCH", { id, status: "Approved" });
  const submit = async (category, date) => {
    const { id } = await api(
      "workspace",
      "Tester",
      "POST",
      leave(category, date),
      201,
    );
    return id;
  };
  const cancel = (who, request_id, reason, expected = 201) =>
    api("cancellations", who, "POST", { request_id, reason }, expected);
  const decide = (who, id, status, expected = 200) =>
    api("cancellations", who, "PATCH", { id, status }, expected);

  // Future approved leave: the employee asks, HR decides, the hours return.
  const future = [weekday(iso(Date.parse(asOf) + 7 * day))];
  future.push(weekday(iso(Date.parse(future[0]) + day)));
  if (future.every((date) => date.startsWith(`${year}-`))) {
    const id = await submit("Annual leave", future[0]);
    await approve(id);
    await cancel("Tester", id, "   ", 400);
    await cancel("Employee", id, "Not mine", 404);
    await cancel("HR", id, "On their behalf", 400);
    const { id: cancellationId } = await cancel("Tester", id, "Plans changed");
    await cancel("Tester", id, "Again", 400);
    assert.equal(await statusOf(id), "Approved", "approved while pending");
    await decide("Tester", cancellationId, "Approved", 403);
    await decide("HR", cancellationId, "Approved");
    await decide("HR", cancellationId, "Approved", 400);
    assert.equal(await statusOf(id), "Cancelled");
    await submit("Annual leave", future[0]);

    const kept = await submit("Annual leave", future[1]);
    await approve(kept);
    const { id: rejectedId } = await cancel("Tester", kept, "Maybe not");
    await decide("HR", rejectedId, "Rejected");
    assert.equal(await statusOf(kept), "Approved");
  }

  // Leave that has started cannot be cancelled by the employee.
  const past = weekday(`${year}-01-06`);
  if (past < asOf) {
    const id = await submit("Annual leave", past);
    await approve(id);
    const refused = await cancel("Tester", id, "Too late", 400);
    assert.match(refused.error, /on hold/);
  }

  // HR corrects started Emergency leave with a reason.
  const emergencyDate = weekday(`${year}-02-02`);
  if (emergencyDate < asOf) {
    const id = await submit("Emergency leave", emergencyDate);
    const form = new FormData();
    form.set("request_id", String(id));
    form.set(
      "file",
      new File([new TextEncoder().encode("%PDF-1.4\n")], "e.pdf"),
    );
    const uploaded = await fetch(`${baseUrl}/api/attachments`, {
      method: "POST",
      headers: { Cookie: sessions.Tester },
      body: form,
    });
    assert.equal(uploaded.status, 201);
    await approve(id);
    await cancel("Tester", id, "Mine", 400);
    const { id: correctionId } = await cancel("HR", id, "Approved in error");
    await decide("HR", correctionId, "Approved");
    assert.equal(await statusOf(id), "Cancelled");
  }

  console.log(
    "PASS: future leave cancelled only by its employee and decided by HR, hours returned, rejected cancellations keep the leave, started leave refused, and HR corrects started Emergency leave.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  if (mark !== undefined) await clearOutboxSince(client, mark);
  const ids = "(SELECT id FROM requests WHERE note = $1)";
  const files = await client.query(
    `SELECT storage_key FROM attachments WHERE request_id IN ${ids}`,
    [prefix],
  );
  for (const { storage_key } of files.rows)
    await unlink(path.resolve(".storage/uploads", storage_key)).catch(() => {});
  for (const table of ["cancellations", "attachments", "leave_entries"])
    await client.query(`DELETE FROM ${table} WHERE request_id IN ${ids}`, [
      prefix,
    ]);
  await client.query("DELETE FROM requests WHERE note = $1", [prefix]);
  await client.query("DELETE FROM employees WHERE email = $1", [email]);
  await client.end();
}
