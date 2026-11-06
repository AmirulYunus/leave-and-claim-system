import assert from "node:assert/strict";
import nextEnv from "@next/env";
import pg from "pg";

nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const prefix = `roles-${Date.now()}`;
const createdEmails = [
  `${prefix}@example.test`,
  `${prefix}-admin@example.test`,
  `${prefix}-hr@example.test`,
];

async function api(path, role, method = "GET", body, expected = 200) {
  const response = await fetch(`http://localhost:3000/api/${path}`, {
    method,
    headers: { "Content-Type": "application/json", "x-demo-role": role },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const result = await response.json();
  assert.equal(response.status, expected, result.error ?? `${method} ${path}`);
  return result;
}

try {
  await client.connect();
  const input = {
    name: "Permission check",
    email: createdEmails[0],
    join_date: "2026-10-01",
    role: "Employee",
  };
  await api("employees", "Employee", "GET", undefined, 403);
  await api("employees", "Employee", "POST", input, 403);
  await api(
    "workspace",
    "Employee",
    "PATCH",
    { id: 1, status: "Approved" },
    403,
  );
  await api("employees", "HR", "POST", { ...input, role: "Admin" }, 403);
  await api(
    "employees",
    "HR",
    "POST",
    { ...input, email: createdEmails[2], role: "HR" },
    201,
  );
  const createdHr = (await api("employees", "HR")).employees.find(
    (person) => person.email === createdEmails[2],
  );
  assert.equal(createdHr.role, "HR");
  await api("employees", "HR", "PATCH", createdHr, 403);
  await api("employees", "HR", "POST", input, 201);
  await api("employees", "HR", "POST", input, 409);
  const listing = await api("employees", "HR");
  const employee = listing.employees.find(
    (person) => person.email === input.email,
  );
  assert.equal(employee.role, "Employee");
  await api("employees", "HR", "PATCH", { ...employee, name: "Updated by HR" });
  await api("employees", "HR", "PATCH", { ...employee, role: "Admin" }, 403);
  await api("employees", "Admin", "PATCH", { ...employee, role: "HR" });
  await api("employees", "HR", "PATCH", employee, 403);
  await api(
    "employees",
    "Admin",
    "POST",
    { ...input, email: createdEmails[1], role: "Admin" },
    201,
  );
  const all = await api("employees", "Admin");
  assert.equal(
    all.employees.find((person) => person.email === createdEmails[1]).role,
    "Admin",
  );
  const mine = await api("workspace", "Employee");
  assert.ok(
    mine.requests.every((request) => request.employee_id === mine.employee.id),
  );
  assert.equal((await api("workspace", "HR")).employee.role, "HR");
  assert.equal((await api("workspace", "Admin")).employee.role, "Admin");
  console.log(
    "PASS: Employee access denied; HR creates Employee and HR profiles, edits Employee profiles, and cannot create Admin or assign roles; Admin creates and assigns roles; requests scoped by employee ID.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  await client.query("DELETE FROM employees WHERE email = ANY($1::text[])", [
    createdEmails,
  ]);
  await client.end();
}
