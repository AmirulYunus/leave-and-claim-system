import test from "node:test";
import assert from "node:assert/strict";
import { unusedAnnualHours } from "../app/lib/annual-ledger.ts";

const utc = (iso) => new Date(`${iso}T00:00:00.000Z`);
const staff = (overrides = {}) => ({
  id: 42,
  join_date: utc("2025-01-15"),
  employee_level: 1,
  employment_type: "Full-time",
  is_active: true,
  leaving_date: null,
  ...overrides,
});
const tx = () => ({
  requests: { findMany: async () => [] },
});

test("a held annual entitlement stays pending instead of zero unused hours", async () => {
  const today = "2026-02-15";
  const year = 2025;
  const cases = [
    ["missing level", staff({ employee_level: null })],
    ["missing employment type", staff({ employment_type: null })],
    ["part-time", staff({ employment_type: "Part-time" })],
    ["intern", staff({ employment_type: "Intern" })],
    ["29 February join", staff({ join_date: utc("2024-02-29") })],
    ["leaving year", staff({ leaving_date: utc("2025-06-30") })],
    ["eight completed years", staff({ join_date: utc("2017-01-01") })],
  ];
  for (const [label, employee] of cases) {
    const unused = await unusedAnnualHours(tx(), employee, year, today);
    assert.equal(
      typeof unused,
      "object",
      `${label} is held, not zero unused hours`,
    );
    assert.equal(unused.hours, undefined, label);
    assert.equal(typeof unused.reason, "string", label);
    assert.ok(unused.reason.length > 0, label);
  }
  assert.deepEqual(await unusedAnnualHours(tx(), staff(), year, today), {
    hours: 112,
  });
});
