import assert from "node:assert/strict";
import nextEnv from "@next/env";
import pg from "pg";
import { baseUrl, demoSessions } from "./sessions.mjs";

// Checks what today's date allows: previews, exception windows and refusals.
// Confirmation, the January–March block and the closed-year lock depend on
// the date and are covered by tests/carry-forward.test.mjs.
nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const prefix = `carry-${Date.now()}`;
const email = `${prefix}@example.test`;
let sessions = {};

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
try {
  await client.connect();
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
  console.log(
    `PASS (${asOf}): carry-forward preview, limits, written exceptions and date windows enforced; employees refused.`,
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  if (employeeId)
    for (const table of ["carry_forward_exceptions", "carry_forwards"])
      await client.query(`DELETE FROM ${table} WHERE employee_id = $1`, [
        employeeId,
      ]);
  await client.query("DELETE FROM employees WHERE email = $1", [email]);
  await client.end();
}
