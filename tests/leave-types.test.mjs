import test from "node:test";
import assert from "node:assert/strict";
import {
  approvalProblem,
  balanceCategory,
  scheduleDates,
  scheduleProblem,
  submissionProblem,
  subtractMonths,
} from "../app/lib/leave-types.ts";

const none = {
  expected_birth_on: null,
  birth_on: null,
  notified_on: null,
  training_category: null,
};
const fullDays = (dates) => dates.map((date) => ({ date, hours: 8 }));

test("maternity and paternity cover fixed consecutive calendar days", () => {
  const maternity = scheduleDates("Maternity leave", "2027-02-01", "");
  assert.equal(maternity.length, 98);
  assert.equal(maternity.at(-1), "2027-05-09");
  const paternity = scheduleDates("Paternity leave", "2026-11-06", "");
  assert.deepEqual(paternity, [
    "2026-11-06",
    "2026-11-07",
    "2026-11-08",
    "2026-11-09",
    "2026-11-10",
    "2026-11-11",
    "2026-11-12",
  ]);
  assert.equal(scheduleProblem("Paternity leave", fullDays(paternity)), "");
  assert.notEqual(
    scheduleProblem("Paternity leave", fullDays(paternity.slice(1))),
    "",
  );
  assert.notEqual(
    scheduleProblem("Paternity leave", [
      ...fullDays(paternity.slice(1)),
      { date: paternity[0], hours: 4 },
    ]),
    "",
  );
});

test("weekday leave uses every weekday with one 4 or 8 hour duration", () => {
  const week = scheduleDates("Medical leave", "2026-11-06", "2026-11-10");
  assert.deepEqual(week, ["2026-11-06", "2026-11-09", "2026-11-10"]);
  for (const category of [
    "Annual leave",
    "Medical leave",
    "Hospitalisation leave",
    "Emergency leave",
    "Training leave",
    "Unpaid leave",
  ]) {
    assert.equal(
      scheduleProblem(
        category,
        week.map((date) => ({ date, hours: 4 })),
      ),
      "",
    );
    assert.notEqual(
      scheduleProblem(category, [
        { date: week[0], hours: 4 },
        { date: week[1], hours: 8 },
      ]),
      "",
    );
    assert.notEqual(
      scheduleProblem(category, [{ date: week[0], hours: 2.5 }]),
      "",
    );
    assert.notEqual(
      scheduleProblem(category, fullDays([week[0], week[2]])),
      "",
      "a skipped weekday",
    );
    assert.notEqual(
      scheduleProblem(category, fullDays(["2026-11-07"])),
      "",
      "a weekend date",
    );
  }
});

test("submission details are required per category", () => {
  assert.notEqual(submissionProblem("Maternity leave", none), "");
  assert.equal(
    submissionProblem("Maternity leave", {
      ...none,
      expected_birth_on: "2027-02-20",
      notified_on: "2026-12-01",
    }),
    "",
  );
  assert.notEqual(
    submissionProblem("Paternity leave", {
      ...none,
      notified_on: "2026-11-01",
    }),
    "",
  );
  assert.equal(
    submissionProblem("Paternity leave", {
      ...none,
      notified_on: "2026-11-01",
      expected_birth_on: "2026-11-06",
    }),
    "",
  );
  assert.notEqual(submissionProblem("Training leave", none), "");
  assert.notEqual(
    submissionProblem("Training leave", {
      ...none,
      training_category: "Other",
    }),
    "",
  );
  assert.equal(
    submissionProblem("Training leave", {
      ...none,
      training_category: "Company Required",
    }),
    "",
  );
  assert.equal(submissionProblem("Emergency leave", none), "");
});

test("emergency and external training are charged to annual leave", () => {
  assert.equal(balanceCategory("Emergency leave", null), "Annual leave");
  assert.equal(
    balanceCategory("Training leave", "Employee-Requested External"),
    "Annual leave",
  );
  assert.equal(balanceCategory("Training leave", "Company Required"), null);
  for (const category of ["Maternity leave", "Paternity leave", "Unpaid leave"])
    assert.equal(balanceCategory(category, null), null);
  assert.equal(balanceCategory("Medical leave", null), "Medical leave");
});

test("emergency approval needs evidence", () => {
  const input = {
    details: none,
    start: "2026-11-02",
    joinDate: "2024-01-01",
    priorBirths: [],
  };
  assert.notEqual(
    approvalProblem("Emergency leave", { ...input, attachments: 0 }),
    "",
  );
  assert.equal(
    approvalProblem("Emergency leave", { ...input, attachments: 1 }),
    "",
  );
});

test("paternity approval checks birth date, service and earlier births", () => {
  const details = {
    ...none,
    notified_on: "2026-10-01",
    birth_on: "2026-11-06",
  };
  const input = {
    details,
    start: "2026-11-06",
    joinDate: "2025-11-06",
    attachments: 0,
    priorBirths: [],
  };
  assert.equal(approvalProblem("Paternity leave", input), "");
  assert.notEqual(
    approvalProblem("Paternity leave", {
      ...input,
      details: { ...details, birth_on: null, expected_birth_on: "2026-11-06" },
    }),
    "",
    "birth date not yet recorded",
  );
  assert.notEqual(
    approvalProblem("Paternity leave", { ...input, start: "2026-11-07" }),
    "",
  );
  assert.notEqual(
    approvalProblem("Paternity leave", { ...input, joinDate: "2025-11-07" }),
    "",
    "one day short of 12 months",
  );
  assert.notEqual(
    approvalProblem("Paternity leave", {
      ...input,
      priorBirths: ["2026-11-06"],
    }),
    "",
  );
  assert.notEqual(
    approvalProblem("Paternity leave", {
      ...input,
      priorBirths: ["2020-01-01", "2021-01-01", "2022-01-01", null, null],
    }),
    "",
  );
  assert.equal(
    approvalProblem("Paternity leave", {
      ...input,
      priorBirths: ["2020-01-01", "2020-01-01", null, null],
    }),
    "",
  );
});

test("twelve months back clamps to the end of a shorter month", () => {
  assert.equal(subtractMonths("2026-11-06", 12), "2025-11-06");
  assert.equal(subtractMonths("2028-02-29", 12), "2027-02-28");
  assert.equal(subtractMonths("2026-03-31", 1), "2026-02-28");
});
