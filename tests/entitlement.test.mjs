// Cases ported from the Dataverse EntitlementChecks and CalendarGrantChecks.
import test from "node:test";
import assert from "node:assert/strict";
import {
  annualEarnedHours,
  cumulativeEarnedHours,
  entitlementRates,
  leaveAllowanceHours,
  malaysiaDate,
} from "../app/lib/entitlement.ts";

const assessment = "2026-10-09";
const calc = (joined, level = 1, type = "Full-time") =>
  entitlementRates(
    { join_date: joined, employee_level: level, employment_type: type },
    assessment,
  );

test("documented level bases are 14/14/15/18 days", () => {
  for (const [level, days] of [
    [1, 14],
    [2, 14],
    [3, 15],
    [4, 18],
  ])
    assert.equal(calc("2025-10-09", level).annual.days, days);
});

test("completed service adds the reward and the statutory floor", () => {
  for (const [years, days] of [
    [1, 14],
    [2, 15],
    [3, 15],
    [4, 16],
    [5, 16],
    [6, 17],
    [7, 17],
  ])
    assert.equal(calc(`${2026 - years}-10-09`).annual.days, days, `${years}y`);
  assert.equal(calc("2024-10-09").annual.hours, 120);
});

test("service anniversaries take effect on the day", () => {
  let rate = calc("2024-10-10");
  assert.equal(rate.completedYears, 1);
  assert.equal(rate.annual.days, 14);
  rate = calc("2024-10-09");
  assert.equal(rate.completedYears, 2);
  assert.equal(rate.annual.days, 15);
});

test("eight or more years is held while medical is calculable", () => {
  const rate = calc("2018-10-09");
  assert.equal(rate.annual.status, "PendingPolicy");
  assert.equal(rate.annual.days, null);
  assert.equal(rate.sick.days, 22);
});

test("missing inputs are pending HR details, never zero", () => {
  let rate = calc(null);
  assert.equal(rate.annual.status, "PendingHrDetails");
  for (const kind of ["annual", "sick", "hospitalisation"])
    assert.equal(rate[kind].days, null);
  rate = calc("2024-10-09", 1, null);
  for (const kind of ["annual", "sick", "hospitalisation"])
    assert.equal(rate[kind].days, null);
  rate = calc("2024-10-09", null);
  assert.equal(rate.annual.days, null);
  assert.equal(rate.sick.days, 18);
  assert.equal(rate.hospitalisation.days, 60);
  const reason = calc(null, null, null).annual.reason;
  for (const field of ["Date Joined", "Employee Level", "Employment Type"])
    assert.ok(reason.includes(field), field);
});

test("part-time and intern rules are held, not zero", () => {
  for (const type of ["Part-time", "Intern"]) {
    const rate = calc("2020-01-01", 1, type);
    assert.equal(rate.annual.days, null);
    assert.equal(rate.sick.status, "PendingPolicy");
    assert.equal(rate.hospitalisation.days, null);
  }
});

test("invalid details cannot fall back to a default", () => {
  assert.equal(calc("2024-10-09", 42).annual.status, "InvalidDetails");
  assert.equal(calc("2024-10-09", 1, "Contract").sick.status, "InvalidDetails");
  assert.equal(calc("2026-10-10").annual.status, "InvalidDetails");
  assert.equal(calc("2024-02-29").annual.status, "PendingPolicy");
});

test("same-day hire has zero completed years and the full-year rate", () => {
  const rate = calc(assessment);
  assert.equal(rate.completedYears, 0);
  assert.equal(rate.annual.days, 14);
});

test("medical thresholds are independent of annual rewards", () => {
  assert.equal(calc("2024-10-10").sick.days, 14);
  assert.equal(calc("2024-10-09").sick.days, 18);
  assert.equal(calc("2021-10-10").sick.days, 18);
  assert.equal(calc("2021-10-09").sick.days, 22);
  assert.equal(calc("2024-10-09").hospitalisation.days, 60);
});

test("Malaysia date rolls over at 16:00 UTC", () => {
  assert.equal(malaysiaDate(new Date("2026-10-08T15:59:00Z")), "2026-10-08");
  assert.equal(malaysiaDate(new Date("2026-10-08T16:00:00Z")), "2026-10-09");
});

test("accrual excludes the unfinished month and rounds the total up once", () => {
  const joined = "2026-02-20";
  const flat = () => 14;
  for (const [asOf, hours] of [
    ["2026-10-14", 76],
    ["2026-11-03", 84],
    ["2026-11-30", 84],
    ["2026-12-01", 96],
    ["2026-12-31", 96],
  ])
    assert.equal(cumulativeEarnedHours(joined, asOf, flat), hours, asOf);
  assert.equal(cumulativeEarnedHours(joined, "2026-12-31", flat, true), 104);
  assert.equal(cumulativeEarnedHours("2022-02-20", "2026-10-14", flat), 84);
  assert.equal(cumulativeEarnedHours("2026-10-20", "2026-10-14", flat), 0);
});

test("an anniversary increase applies from its month, not earlier", () => {
  const rate = (monthEnd) => (monthEnd >= "2026-07-15" ? 20 : 14);
  for (const [asOf, hours] of [
    ["2026-06-30", 48],
    ["2026-07-14", 56],
    ["2026-07-31", 56],
    ["2026-08-01", 72],
  ])
    assert.equal(cumulativeEarnedHours("2022-07-15", asOf, rate), hours, asOf);
});

const february = {
  join_date: "2026-02-20",
  employee_level: 1,
  employment_type: "Full-time",
  is_active: true,
  leaving_date: null,
};

test("earned leave uses today's completed months, not the leave date", () => {
  for (const [year, today, hours] of [
    [2026, "2026-10-14", 76],
    [2027, "2026-10-14", 0],
    [2026, "2026-11-30", 84],
    [2026, "2026-12-01", 96],
    [2026, "2027-01-01", 104],
  ])
    assert.equal(
      annualEarnedHours(february, year, today).hours,
      hours,
      `${year} on ${today}`,
    );
});

test("each leave category maps to its allowance", () => {
  const today = "2026-10-14";
  assert.equal(
    leaveAllowanceHours(february, "Annual leave", 2026, today).hours,
    76,
  );
  assert.equal(
    leaveAllowanceHours(february, "Medical leave", 2026, today).hours,
    112,
  );
  assert.equal(
    leaveAllowanceHours(february, "Hospitalisation leave", 2026, today).hours,
    480,
  );
  assert.equal(
    leaveAllowanceHours(february, "Unpaid leave", 2026, today).hours,
    Infinity,
  );
  const pending = { ...february, employment_type: null };
  for (const category of [
    "Annual leave",
    "Medical leave",
    "Hospitalisation leave",
  ])
    assert.ok(
      leaveAllowanceHours(pending, category, 2026, today).reason,
      category,
    );
  assert.ok(
    leaveAllowanceHours(
      { ...february, is_active: false },
      "Medical leave",
      2026,
      today,
    ).reason,
  );
});

test("earned leave is held for missing inputs and the leaving year", () => {
  for (const employee of [
    { ...february, employee_level: null },
    { ...february, is_active: false },
    { ...february, leaving_date: "2026-12-31" },
    { ...february, join_date: "2018-01-01" },
  ])
    assert.ok(annualEarnedHours(employee, 2026, "2026-10-10").reason);
});
