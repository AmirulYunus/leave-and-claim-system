import assert from "node:assert/strict";
import nextEnv from "@next/env";
import pg from "pg";
import { baseUrl, demoSessions } from "./sessions.mjs";
nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const marker = `Persistence check ${Date.now()}`;
let id;
let sessions = {};
const headers = (role) => ({
  "Content-Type": "application/json",
  Cookie: sessions[role],
});
async function api(method, body) {
  const response = await fetch(`${baseUrl}/api/workspace`, {
    method,
    headers: headers(method === "PATCH" ? "HR" : "Employee"),
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const data = await response.json();
  assert.ok(response.ok, data.error ?? `HTTP ${response.status}`);
  return data;
}
try {
  await client.connect();
  sessions = await demoSessions();
  const before = await api("GET");
  const date = "2026-12-29";
  assert.ok(
    !before.requests.some(
      (r) =>
        r.employee_id === before.employee.id &&
        r.status !== "Rejected" &&
        r.entries.some((e) => e.date === date),
    ),
    "Test date already has leave; choose a clear date.",
  );
  assert.ok(
    !before.worked[date],
    "Test date already has worked hours; choose a clear date.",
  );
  await api("POST", {
    kind: "Leave",
    category: "Annual leave",
    detail: "29 Dec 2026",
    note: marker,
    entries: [{ date, hours: 4 }],
  });
  const fractional = await fetch(`${baseUrl}/api/workspace`, {
    method: "POST",
    headers: headers("Employee"),
    body: JSON.stringify({
      kind: "Leave",
      category: "Annual leave",
      detail: "29 Dec 2026",
      note: marker,
      entries: [{ date, hours: 2.5 }],
    }),
  });
  assert.equal(fractional.status, 400, "only 4 or 8 hours per date");
  const saved = await api("GET");
  id = saved.requests.find((r) => r.note === marker).id;
  assert.equal(saved.requests.find((r) => r.id === id).value, 0.5);
  await api("PATCH", { id, status: "Approved" });
  const review = await client.query(
    "SELECT e.role, e.email FROM requests r JOIN employees e ON e.id=r.reviewed_by WHERE r.id=$1",
    [id],
  );
  assert.equal(review.rows[0].role, "HR");
  assert.equal(review.rows[0].email, "hr@example.test");
  await api("PUT", { month: "2026-12", worked: { [date]: "4" } });
  const refreshed = await api("GET");
  assert.equal(refreshed.requests.find((r) => r.id === id).status, "Approved");
  assert.equal(Number(refreshed.worked[date]), 4);
  const invalid = await fetch(`${baseUrl}/api/workspace`, {
    method: "PUT",
    headers: headers("Employee"),
    body: JSON.stringify({ month: "2026-12", worked: { [date]: "4.5" } }),
  });
  assert.equal(invalid.status, 400);
  const again = await api("GET");
  assert.equal(Number(again.worked[date]), 4);
  const duplicate = await fetch(`${baseUrl}/api/workspace`, {
    method: "PATCH",
    headers: headers("HR"),
    body: JSON.stringify({ id, status: "Approved" }),
  });
  assert.equal(duplicate.status, 400);
  console.log(
    "PASS: half-day leave, HR review, worked hours persist across fresh reads; fractional leave, conflicting hours and repeated reviews rejected.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  if (id) {
    await client.query("BEGIN");
    await client.query("DELETE FROM leave_entries WHERE request_id=$1", [id]);
    await client.query("DELETE FROM requests WHERE id=$1 AND note=$2", [
      id,
      marker,
    ]);
    await client.query(
      "DELETE FROM timesheet_entries WHERE date='2026-12-29' AND employee_id=(SELECT id FROM employees WHERE email='amirul@example.test')",
    );
    await client.query("COMMIT");
  }
  await client.end();
}
