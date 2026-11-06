import assert from "node:assert/strict";
import nextEnv from "@next/env";
import pg from "pg";
import { baseUrl, demoSessions, signIn } from "./sessions.mjs";

nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const prefix = `entitlements-${Date.now()}`;
const email = `${prefix}@example.test`;
let sessions = {};

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

try {
  await client.connect();
  sessions = await demoSessions();
  const { entitlements } = await api("workspace", "Employee");
  const year = entitlements.year;
  assert.equal(entitlements.asOf.slice(0, 4), String(year));
  const { annual, medical, hospitalisation } = entitlements;
  assert.ok(annual.rateDays >= 14, "annual rate");
  assert.equal(annual.hours % 4, 0, "earned annual leave is in half days");
  assert.ok(annual.hours <= annual.rateDays * 8, "earned within the rate");
  assert.equal(medical.hours, medical.rateDays * 8);
  assert.equal(hospitalisation.hours, 480);

  const leave = (date, category = "Annual leave") => ({
    kind: "Leave",
    category,
    detail: date,
    note: prefix,
    entries: [{ date, hours: 8 }],
  });
  const nextYear = await api(
    "workspace",
    "Employee",
    "POST",
    leave(`${year + 1}-01-05`),
    400,
  );
  assert.match(nextYear.error, new RegExp(`${year} dates only`));

  await api(
    "employees",
    "HR",
    "POST",
    {
      name: "Entitlement check",
      email,
      join_date: `${year - 1}-03-01`,
      temporary_password: "temporary-pass-1",
    },
    201,
  );
  const session = await signIn(email, "temporary-pass-1");
  await api("auth/password", session, "POST", {
    current_password: "temporary-pass-1",
    new_password: "chosen-password-1",
  });
  const pending = (await api("workspace", session)).entitlements;
  assert.equal(pending.annual.hours, null);
  assert.match(pending.annual.reason, /employment details/);
  assert.equal(pending.medical.hours, null);
  assert.match(pending.medical.reason, /Employment Type/);
  const weekday = `${year}-12-01`;
  const refused = await api("workspace", session, "POST", leave(weekday), 400);
  assert.match(refused.error, /Annual leave is unavailable/);
  await api("workspace", session, "POST", leave(weekday, "Unpaid leave"), 201);

  console.log(
    "PASS: entitlements returned in half-day earned hours; next-year dates refused; missing HR details block entitled leave but not Unpaid leave.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  await client.query(
    "DELETE FROM leave_entries WHERE request_id IN (SELECT id FROM requests WHERE note = $1)",
    [prefix],
  );
  await client.query("DELETE FROM requests WHERE note = $1", [prefix]);
  await client.query("DELETE FROM employees WHERE email = $1", [email]);
  await client.end();
}
