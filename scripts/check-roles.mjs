import assert from "node:assert/strict";
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
const prefix = `roles-${Date.now()}`;
const createdEmails = [
  `${prefix}@example.test`,
  `${prefix}-admin@example.test`,
  `${prefix}-hr@example.test`,
];

let sessions = {};
let mark;
// `who` is a demo role name or the Cookie header of another session.
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
  mark = await outboxMark(client);
  sessions = await demoSessions();
  const input = {
    name: "Permission check",
    email: createdEmails[0],
    join_date: "2026-10-01",
    role: "Employee",
    temporary_password: "temporary-pass-1",
  };
  await api("workspace", "", "GET", undefined, 401);
  for (const [origin, expected] of [
    [baseUrl, 200],
    [baseUrl.replace("localhost", "127.0.0.1"), 403],
    ["http://cross-site.invalid", 403],
  ]) {
    const response = await fetch(`${baseUrl}/api/auth/session`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: origin },
      body: JSON.stringify({
        email: "hr@example.test",
        password: process.env.SEED_PASSWORD,
      }),
    });
    assert.equal(response.status, expected, `sign-in from ${origin}`);
  }
  const spoofed = await fetch(`${baseUrl}/api/auth/session`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Origin: "https://evil.example",
      "X-Forwarded-Host": "evil.example",
    },
    body: JSON.stringify({
      email: "hr@example.test",
      password: process.env.SEED_PASSWORD,
    }),
  });
  assert.equal(spoofed.status, 403, "sign-in with a spoofed X-Forwarded-Host");
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
  await api(
    "employees",
    "HR",
    "POST",
    { ...input, temporary_password: undefined },
    400,
  );
  await api("employees", "HR", "POST", input, 201);
  await api("employees", "HR", "POST", input, 409);
  const listing = await api("employees", "HR");
  assert.ok(listing.employees.every((person) => !("password_hash" in person)));
  const employee = listing.employees.find(
    (person) => person.email === input.email,
  );
  assert.equal(employee.role, "Employee");
  assert.equal(employee.must_change_password, true);

  const firstSession = await signIn(input.email, input.temporary_password);
  const pending = await api("workspace", firstSession, "GET", undefined, 403);
  assert.equal(pending.code, "PASSWORD_CHANGE_REQUIRED");
  await api(
    "auth/password",
    firstSession,
    "POST",
    { current_password: "not-the-password", new_password: "chosen-password-1" },
    400,
  );
  await api("auth/password", firstSession, "POST", {
    current_password: input.temporary_password,
    new_password: "chosen-password-1",
  });
  await api("workspace", firstSession);
  await api("employees", "HR", "PATCH", {
    ...employee,
    temporary_password: "reset-password-1",
  });
  await api("workspace", firstSession, "GET", undefined, 401);
  await assert.rejects(signIn(input.email, "chosen-password-1"));
  assert.equal(employee.employee_level, null);
  assert.equal(employee.is_active, true);

  const [nameA, nameB] = [`${prefix} A`, `${prefix} B`];
  await api(
    "organisation",
    "Employee",
    "POST",
    { type: "department", name: nameA },
    403,
  );
  for (const name of [nameA, nameB])
    await api("organisation", "HR", "POST", { type: "department", name }, 201);
  await api(
    "organisation",
    "HR",
    "POST",
    { type: "costCentre", name: nameA },
    201,
  );
  await api(
    "organisation",
    "HR",
    "POST",
    { type: "department", name: nameA },
    409,
  );
  const lists = await api("organisation", "HR");
  const [deptA, deptB] = [nameA, nameB].map((name) =>
    lists.departments.find((entry) => entry.name === name),
  );
  const centre = lists.costCentres.find((entry) => entry.name === nameA);
  const reload = async () =>
    (await api("employees", "HR")).employees.find(
      (person) => person.email === input.email,
    );
  await api(
    "employees",
    "HR",
    "PATCH",
    { ...employee, employee_level: 5 },
    400,
  );
  await api(
    "employees",
    "HR",
    "PATCH",
    { ...employee, leaving_date: "2026-09-30" },
    400,
  );
  await api("employees", "HR", "PATCH", {
    ...employee,
    employment_type: "Full-time",
    employee_level: "2",
    department_id: deptA.id,
    cost_centre_id: centre.id,
  });
  await api("organisation", "HR", "PATCH", {
    type: "department",
    id: deptB.id,
    name: deptB.name,
    is_active: false,
  });
  const classified = await reload();
  await api(
    "employees",
    "HR",
    "PATCH",
    { ...classified, department_id: deptB.id },
    400,
  );
  await api("organisation", "HR", "PATCH", {
    type: "department",
    id: deptA.id,
    name: deptA.name,
    is_active: false,
  });
  await api("employees", "HR", "PATCH", { ...classified, position: "Analyst" });
  const kept = await reload();
  assert.deepEqual(
    [
      kept.employment_type,
      kept.employee_level,
      kept.department_id,
      kept.cost_centre_id,
      kept.position,
    ],
    ["Full-time", 2, deptA.id, centre.id, "Analyst"],
  );

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

  await api(
    "workspace",
    "HR",
    "POST",
    {
      kind: "Claim",
      category: "Travel",
      detail: "Self-approval check",
      note: prefix,
      value: 12.5,
    },
    201,
  );
  const own = (await api("workspace", "HR")).requests.find(
    (request) => request.note === prefix,
  );
  await api(
    "workspace",
    "HR",
    "PATCH",
    { id: own.id, status: "Approved" },
    403,
  );
  await api("workspace", "Admin", "PATCH", { id: own.id, status: "Approved" });
  console.log(
    "PASS: sign-in required; temporary passwords must be changed and can be reset; Employee access denied; HR creates Employee and HR profiles, edits Employee profiles and employment details, maintains departments and cost centres, and cannot create Admin or assign roles; Admin creates and assigns roles; requests scoped by employee ID; nobody decides their own request.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  if (mark !== undefined) await clearOutboxSince(client, mark);
  await client.query(
    "DELETE FROM claims WHERE request_id IN (SELECT id FROM requests WHERE note = $1)",
    [prefix],
  );
  await client.query("DELETE FROM requests WHERE note = $1", [prefix]);
  await client.query("DELETE FROM employees WHERE email = ANY($1::text[])", [
    createdEmails,
  ]);
  for (const table of ["departments", "cost_centres"])
    await client.query(`DELETE FROM ${table} WHERE name LIKE $1`, [
      `${prefix} %`,
    ]);
  await client.end();
}
