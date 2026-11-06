import assert from "node:assert/strict";
import { unlink } from "node:fs/promises";
import path from "node:path";
import nextEnv from "@next/env";
import pg from "pg";
import { baseUrl, demoSessions, signIn } from "./sessions.mjs";

nextEnv.loadEnvConfig(process.cwd());
const client = new pg.Client({ connectionString: process.env.DATABASE_URL });
const prefix = `leave-types-${Date.now()}`;
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
async function upload(who, requestId, name, bytes, expected = 201) {
  const form = new FormData();
  form.set("request_id", String(requestId));
  form.set("file", new File([bytes], name));
  const response = await fetch(`${baseUrl}/api/attachments`, {
    method: "POST",
    headers: { Cookie: sessions[who] ?? who },
    body: form,
  });
  const result = await response.json();
  assert.equal(response.status, expected, result.error ?? "upload");
  return result;
}

const day = 86400_000;
const iso = (time) => new Date(time).toISOString().slice(0, 10);
const calendarDays = (start, count) =>
  Array.from({ length: count }, (_, i) => iso(Date.parse(start) + i * day));
function weekdays(start, count) {
  const dates = [];
  for (let t = Date.parse(start); dates.length < count; t += day)
    if (![0, 6].includes(new Date(t).getUTCDay())) dates.push(iso(t));
  return dates;
}
const leave = (category, dates, hours = 8, details = {}) => ({
  kind: "Leave",
  category,
  detail: `${dates[0]} to ${dates.at(-1)}`,
  note: prefix,
  entries: dates.map((date) => ({ date, hours })),
  ...details,
});
const pdf = new TextEncoder().encode("%PDF-1.4\n%%EOF\n");

try {
  await client.connect();
  sessions = await demoSessions();
  const year = (await api("workspace", "Employee")).entitlements.year;
  await api(
    "employees",
    "HR",
    "POST",
    {
      name: "Leave type check",
      email,
      join_date: `${year - 3}-01-01`,
      employment_type: "Full-time",
      employee_level: 1,
      probation_completed_on: `${year - 3}-04-01`,
      temporary_password: "temporary-pass-1",
    },
    201,
  );
  sessions.Tester = await signIn(email, "temporary-pass-1");
  await api("auth/password", "Tester", "POST", {
    current_password: "temporary-pass-1",
    new_password: "chosen-password-1",
  });
  const decide = (id, extra = {}, expected = 200) =>
    api(
      "workspace",
      "HR",
      "PATCH",
      { id, status: "Approved", ...extra },
      expected,
    );

  // Maternity: 98 consecutive days with confinement and notice dates.
  const maternity = calendarDays(`${year + 1}-02-01`, 98);
  await api(
    "workspace",
    "Tester",
    "POST",
    leave("Maternity leave", maternity),
    400,
  );
  await api(
    "workspace",
    "Tester",
    "POST",
    leave("Maternity leave", maternity.slice(0, 97), 8, {
      expected_birth_on: `${year + 1}-02-20`,
      notified_on: `${year}-12-01`,
    }),
    400,
  );
  const { id: maternityId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Maternity leave", maternity, 8, {
      expected_birth_on: `${year + 1}-02-20`,
      notified_on: `${year}-12-01`,
    }),
    201,
  );
  await decide(maternityId);

  // Paternity: 7 days that HR approves only from the verified birth date.
  const paternity = calendarDays(`${year + 1}-07-05`, 7);
  await api(
    "workspace",
    "Tester",
    "POST",
    leave("Paternity leave", paternity, 8, {
      notified_on: `${year + 1}-06-01`,
    }),
    400,
  );
  const { id: paternityId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Paternity leave", paternity, 8, {
      notified_on: `${year + 1}-06-01`,
      expected_birth_on: paternity[0],
    }),
    201,
  );
  await decide(paternityId, {}, 400);
  await decide(paternityId, { birth_on: paternity[1] }, 400);
  await decide(paternityId, { birth_on: paternity[0] });

  // Training: category required, one duration for every weekday.
  const training = weekdays(`${year}-03-02`, 2);
  await api(
    "workspace",
    "Tester",
    "POST",
    leave("Training leave", training),
    400,
  );
  await api(
    "workspace",
    "Tester",
    "POST",
    {
      ...leave("Training leave", training, 8, {
        training_category: "Company Required",
      }),
      entries: [
        { date: training[0], hours: 4 },
        { date: training[1], hours: 8 },
      ],
    },
    400,
  );
  const { id: externalId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Training leave", training, 8, {
      training_category: "Employee-Requested External",
    }),
    201,
  );
  await decide(externalId);

  // External training was charged to Annual Leave, so an emergency request
  // that fits the earned balance on its own is now refused.
  const earned = (await api("workspace", "Tester")).entitlements.annual.hours;
  const fits = weekdays(`${year}-01-05`, Math.floor(earned / 8));
  const over = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Emergency leave", fits),
    400,
  );
  assert.match(over.error, /exceeds your balance/);

  // Emergency: can be past-dated, needs evidence before approval.
  const emergency = weekdays(`${year}-04-06`, 1);
  const { id: emergencyId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Emergency leave", emergency),
    201,
  );
  await decide(emergencyId, {}, 400);
  await upload(
    "Tester",
    emergencyId,
    "renamed.pdf",
    new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    400,
  );
  await upload("Employee", emergencyId, "evidence.pdf", pdf, 404);
  const { id: attachmentId } = await upload(
    "Tester",
    emergencyId,
    "evidence.pdf",
    pdf,
  );
  await decide(emergencyId);
  await upload("Tester", emergencyId, "late.pdf", pdf, 400);
  for (const [who, status] of [
    ["Tester", 200],
    ["HR", 200],
    ["Employee", 404],
  ]) {
    const response = await fetch(`${baseUrl}/api/attachments/${attachmentId}`, {
      headers: { Cookie: sessions[who] },
    });
    assert.equal(response.status, status, `download as ${who}`);
    if (status === 200)
      assert.deepEqual(new Uint8Array(await response.arrayBuffer()), pdf);
  }

  // Medical: within 48 hours of the start date, certificate before approval.
  const { asOf } = (await api("workspace", "Tester")).entitlements;
  const sameYear = (date) => date.startsWith(`${year}-`);
  const late = weekdays(iso(Date.parse(asOf) - 10 * day), 1);
  if (sameYear(late[0])) {
    const refused = await api(
      "workspace",
      "Tester",
      "POST",
      leave("Medical leave", late),
      400,
    );
    assert.match(refused.error, /48 hours/);
  }
  const sick = weekdays(asOf, 1);
  if (sameYear(sick[0])) {
    const { id: medicalId } = await api(
      "workspace",
      "Tester",
      "POST",
      leave("Medical leave", sick),
      201,
    );
    await decide(medicalId, {}, 400);
    await upload("Tester", medicalId, "certificate.pdf", pdf);
    await decide(medicalId);
  }

  // Marriage: first legal marriage, within 12 months of registration,
  // certificate before approval, approved once only.
  const marriage = calendarDays(`${year}-05-02`, 3);
  const married = { registered_on: `${year}-05-01` };
  await api(
    "workspace",
    "Tester",
    "POST",
    leave("Marriage leave", marriage, 8, married),
    400,
  );
  const { id: marriageId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Marriage leave", marriage, 8, { ...married, first_marriage: true }),
    201,
  );
  await decide(marriageId, {}, 400);
  await upload("Tester", marriageId, "certificate.pdf", pdf);
  await decide(marriageId);
  const { id: secondId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Marriage leave", calendarDays(`${year}-05-10`, 3), 8, {
      ...married,
      first_marriage: true,
    }),
    201,
  );
  await upload("Tester", secondId, "certificate.pdf", pdf);
  const repeat = await decide(secondId, {}, 400);
  assert.match(repeat.error, /already approved/);

  // Compassionate: an eligible relationship and a supporting document.
  const compassionate = calendarDays(`${year}-06-01`, 3);
  await api(
    "workspace",
    "Tester",
    "POST",
    leave("Compassionate leave", compassionate, 8, { relationship: "Cousin" }),
    400,
  );
  const { id: compassionateId } = await api(
    "workspace",
    "Tester",
    "POST",
    leave("Compassionate leave", compassionate, 8, { relationship: "Sibling" }),
    201,
  );
  await decide(compassionateId, {}, 400);
  await upload("Tester", compassionateId, "notice.pdf", pdf);
  await decide(compassionateId);

  const saved = (await api("workspace", "Tester")).requests.find(
    (request) => request.id === paternityId,
  );
  assert.equal(saved.details.birth_on, paternity[0]);
  console.log(
    "PASS: maternity, paternity, marriage and compassionate rules, training categories and Annual charging, emergency and medical evidence, the 48-hour medical rule, and document access.",
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  const ids = "(SELECT id FROM requests WHERE note = $1)";
  const files = await client.query(
    `SELECT storage_key FROM attachments WHERE request_id IN ${ids}`,
    [prefix],
  );
  for (const { storage_key } of files.rows)
    await unlink(path.resolve(".storage/uploads", storage_key)).catch(() => {});
  for (const table of ["attachments", "leave_entries"])
    await client.query(`DELETE FROM ${table} WHERE request_id IN ${ids}`, [
      prefix,
    ]);
  await client.query("DELETE FROM requests WHERE note = $1", [prefix]);
  await client.query("DELETE FROM employees WHERE email = $1", [email]);
  await client.end();
}
