import test from "node:test";
import assert from "node:assert/strict";
import {
  annualAllowance,
  sickAllowance,
  datesBetween,
  isWeekday,
  validateLeave,
} from "../app/lib/time.ts";
test("company allowance respects the five-year statutory boundary", () => {
  assert.equal(annualAllowance("2021-10-06", "2026-10-05"), 14);
  assert.equal(annualAllowance("2021-10-05", "2026-10-05"), 16);
  assert.equal(sickAllowance("2024-10-06", "2026-10-05"), 14);
  assert.equal(sickAllowance("2024-10-05", "2026-10-05"), 18);
  assert.equal(sickAllowance("2021-10-05", "2026-10-05"), 22);
});
test("annual leave excludes weekends and accepts four-hour requests", () => {
  assert.deepEqual(datesBetween("2026-10-09", "2026-10-12").filter(isWeekday), [
    "2026-10-09",
    "2026-10-12",
  ]);
  assert.equal(validateLeave([{ date: "2026-10-09", hours: 4 }], [], 112), "");
  assert.notEqual(validateLeave([{ date: "2026-10-09", hours: 4 }], [], 3), "");
});
test("invalid dates and overlapping hours cannot pass validation", () => {
  assert.deepEqual(datesBetween("2026-02-30", "2026-03-03"), []);
  assert.deepEqual(datesBetween("2026-10-12", "2026-10-09"), []);
  assert.notEqual(
    validateLeave(
      [{ date: "2026-10-09", hours: 5 }],
      [{ date: "2026-10-09", hours: 4 }],
      112,
    ),
    "",
  );
  assert.equal(
    validateLeave(
      [{ date: "2026-10-09", hours: 4 }],
      [{ date: "2026-10-09", hours: 4 }],
      112,
    ),
    "",
  );
  for (const hours of [0, -1, 9, NaN])
    assert.notEqual(
      validateLeave([{ date: "2026-10-09", hours }], [], 112),
      "",
    );
});

test("arbitrary fractional annual leave hours are accepted", () => {
  for (const hours of [0.25, 1, 2.5, 3, 6, 7.75]) {
    assert.equal(validateLeave([{ date: "2026-10-09", hours }], [], 112), "");
  }
});
