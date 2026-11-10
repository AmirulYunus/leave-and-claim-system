import assert from "node:assert/strict";
import nextEnv from "@next/env";
import pg from "pg";
import {
  baseUrl,
  clearOutboxSince,
  demoSessions,
  outboxMark,
} from "./sessions.mjs";

// Checks what today's date allows: previews, exception windows and refusals.
// Confirmation, the January–March block and the closed-year lock depend on
// the date and are covered by tests/carry-forward.test.mjs.
nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const prefix = `carry-${Date.now()}`;
const email = `${prefix}@example.test`;
let sessions = {};
let mark;
const malaysiaToday = () =>
  new Date(Date.now() + 8 * 3600_000).toISOString().slice(0, 10);

async function api(path, who, method = "GET", body, expected = 200) {
  const response = await fetch(`${baseUrl}/api/${path}`, {
    method,
    headers: { "Content-Type": "application/json", Cookie: sessions[who] },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const result = await response.json();
  assert.equal(response.status, expected, result.error ?? `${method} ${path}`);
  return result;
}

let employeeId;
let blankEmail;
let blankEmployeeId;
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
      name: "Carry-forward check",
      email,
      join_date: `${year - 3}-01-01`,
      employment_type: "Full-time",
      employee_level: 1,
      temporary_password: "temporary-pass-1",
    },
    201,
  );
  await api("carry-forward", "Employee", "GET", undefined, 403);
  const view = await api("carry-forward", "HR");
  const today = malaysiaToday();
  const currentYear = Number(today.slice(0, 4));
  const defaultYear = today.slice(5) <= "03-31" ? currentYear - 1 : currentYear;
  assert.equal(
    view.sourceYear,
    defaultYear,
    "GET /api/carry-forward with no year uses the default source year",
  );
  assert.equal(view.provisional, defaultYear >= currentYear);
  assert.equal(
    view.canRecordException,
    today >= `${defaultYear}-10-01` && today <= `${defaultYear}-12-15`,
  );
  assert.equal(
    view.canConfirm,
    today >= `${defaultYear + 1}-01-01` && today <= `${defaultYear + 1}-03-31`,
  );
  const row = view.rows.find((r) => r.name === "Carry-forward check");
  employeeId = row.employee_id;
  assert.equal(row.draft.limit, 40);
  assert.equal(row.draft.carry, Math.min(40, row.draft.unusedHours));
  assert.equal(row.confirmed, null);

  const action = (body, expected) =>
    api(
      "carry-forward",
      "HR",
      "POST",
      { employee_id: employeeId, source_year: view.sourceYear, ...body },
      expected,
    );
  await api(
    "carry-forward",
    "Employee",
    "POST",
    { action: "exception", employee_id: employeeId, source_year: year },
    403,
  );
  if (!view.canConfirm) await action({ action: "confirm" }, 400);
  if (view.canRecordException) {
    await action(
      { action: "exception", limit_hours: 42, document_reference: "Memo" },
      400,
    );
    await action(
      { action: "exception", limit_hours: 56, document_reference: " " },
      400,
    );
    await action(
      { action: "exception", limit_hours: 56, document_reference: "Memo 7" },
      201,
    );
    await action(
      { action: "exception", limit_hours: 64, document_reference: "Memo 8" },
      409,
    );
    const updated = (await api("carry-forward", "HR")).rows.find(
      (r) => r.employee_id === employeeId,
    );
    assert.equal(updated.draft.limit, 56);
    assert.equal(updated.exception.document_reference, "Memo 7");
  } else {
    await action(
      { action: "exception", limit_hours: 56, document_reference: "Memo" },
      400,
    );
  }
  const own = (await api("workspace", "Employee")).entitlements.carryForward;
  assert.equal(own.useBy, `${year}-03-31`);
  assert.ok(own.remaining <= own.hours);
  blankEmail = `${prefix}-blank@example.test`;
  await api(
    "employees",
    "HR",
    "POST",
    {
      name: `Blank ${prefix}`,
      email: blankEmail,
      join_date: `${year - 1}-06-01`,
      temporary_password: "temporary-pass-1",
    },
    201,
  );
  const blankView = await api("carry-forward", "HR");
  const blank = blankView.rows.find((r) => r.name === `Blank ${prefix}`);
  assert.ok(blank, "active employee with blank HR details is listed");
  blankEmployeeId = blank.employee_id;
  assert.equal(
    blank.draft,
    null,
    "blank HR details stay held, not a zero draft",
  );
  assert.equal(typeof blank.held, "string");
  assert.ok(blank.held.length > 0);
  if (blankView.canConfirm) {
    const refused = await api(
      "carry-forward",
      "HR",
      "POST",
      {
        action: "confirm",
        employee_id: blank.employee_id,
        source_year: blankView.sourceYear,
      },
      400,
    );
    assert.match(refused.error, /on hold/);
    assert.ok(refused.error.includes(String(blankView.sourceYear)));
    assert.ok(refused.error.includes(blank.held));
  } else {
    console.log(
      `Confirm window is closed on ${today}; asserted the held GET row and did not confirm.`,
    );
  }
  console.log(
    `PASS (${asOf}): carry-forward preview, limits, written exceptions and date windows enforced; employees refused.`,
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  if (mark !== undefined) await clearOutboxSince(client, mark);
  for (const id of [employeeId, blankEmployeeId])
    if (id)
      for (const table of ["carry_forward_exceptions", "carry_forwards"])
        await client.query(`DELETE FROM ${table} WHERE employee_id = $1`, [id]);
  for (const address of [email, blankEmail])
    if (address)
      await client.query("DELETE FROM employees WHERE email = $1", [address]);
  await client.end();
}
