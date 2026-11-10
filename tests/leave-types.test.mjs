import test from "node:test";
import assert from "node:assert/strict";
import {
  addMonths,
  approvalProblem,
  balanceCategory,
  medicalSubmissionProblem,
  scheduleDates,
  scheduleProblem,
  submissionProblem,
} from "../app/lib/leave-types.ts";

const none = {
  expected_birth_on: null,
  birth_on: null,
  notified_on: null,
  training_category: null,
  registered_on: null,
  first_marriage: null,
  relationship: null,
};
const fullDays = (dates) => dates.map((date) => ({ date, hours: 8 }));
const submit = (category, details, start = "2026-11-02", end = start) =>
  submissionProblem(category, { ...none, ...details }, start, end);
const approval = {
  details: none,
  start: "2026-11-02",
  end: "2026-11-02",
  joinDate: "2024-01-01",
  probationCompletedOn: "2024-04-01",
  attachments: 0,
  priorBirths: [],
  priorMarriages: 0,
};

test("fixed-length leave covers consecutive calendar days", () => {
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
  for (const category of ["Marriage leave", "Compassionate leave"])
    assert.deepEqual(scheduleDates(category, "2026-11-06", ""), [
      "2026-11-06",
      "2026-11-07",
      "2026-11-08",
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

test("an empty weekday schedule returns a message", () => {
  assert.equal(
    scheduleProblem("Annual leave", []),
    "Annual leave uses one duration for every date: half day (4 hours) or full day (8 hours).",
  );
});

test("an empty fixed-length schedule returns a message", () => {
  assert.equal(
    scheduleProblem("Paternity leave", []),
    "Paternity leave covers 7 consecutive calendar days in full days.",
  );
});

test("submission details are required per category", () => {
  assert.notEqual(submit("Maternity leave", {}), "");
  assert.equal(
    submit("Maternity leave", {
      expected_birth_on: "2027-02-20",
      notified_on: "2026-12-01",
    }),
    "",
  );
  assert.notEqual(submit("Paternity leave", { notified_on: "2026-11-01" }), "");
  assert.equal(
    submit("Paternity leave", {
      notified_on: "2026-11-01",
      expected_birth_on: "2026-11-06",
    }),
    "",
  );
  assert.notEqual(submit("Training leave", {}), "");
  assert.notEqual(submit("Training leave", { training_category: "Other" }), "");
  assert.equal(
    submit("Training leave", { training_category: "Company Required" }),
    "",
  );
  assert.equal(submit("Emergency leave", {}), "");
});

test("marriage leave falls after registration and within twelve months", () => {
  const first = { registered_on: "2026-05-01", first_marriage: true };
  assert.notEqual(submit("Marriage leave", {}, "2026-05-02", "2026-05-04"), "");
  assert.notEqual(
    submit(
      "Marriage leave",
      { ...first, first_marriage: false },
      "2026-05-02",
      "2026-05-04",
    ),
    "",
    "not declared a first marriage",
  );
  assert.equal(submit("Marriage leave", first, "2026-05-01", "2026-05-03"), "");
  assert.equal(submit("Marriage leave", first, "2027-04-28", "2027-04-30"), "");
  assert.notEqual(
    submit("Marriage leave", first, "2026-04-30", "2026-05-02"),
    "",
    "before registration",
  );
  assert.notEqual(
    submit("Marriage leave", first, "2027-04-29", "2027-05-01"),
    "",
    "ends twelve months after registration",
  );
});

test("compassionate leave needs an eligible relationship", () => {
  assert.notEqual(submit("Compassionate leave", {}), "");
  assert.notEqual(
    submit("Compassionate leave", { relationship: "Cousin" }),
    "",
  );
  for (const relationship of ["Spouse", "Sibling", "Legal guardian"])
    assert.equal(submit("Compassionate leave", { relationship }), "");
});

test("emergency and external training are charged to annual leave", () => {
  assert.equal(balanceCategory("Emergency leave", null), "Annual leave");
  assert.equal(
    balanceCategory("Training leave", "Employee-Requested External"),
    "Annual leave",
  );
  assert.equal(balanceCategory("Training leave", "Company Required"), null);
  for (const category of [
    "Maternity leave",
    "Paternity leave",
    "Marriage leave",
    "Compassionate leave",
    "Unpaid leave",
  ])
    assert.equal(balanceCategory(category, null), null);
  assert.equal(balanceCategory("Medical leave", null), "Medical leave");
});

test("approval needs a document for evidence-based categories", () => {
  const cases = [
    ["Medical leave", {}],
    ["Hospitalisation leave", {}],
    ["Emergency leave", {}],
    ["Compassionate leave", { relationship: "Child" }],
    ["Marriage leave", { registered_on: "2026-11-01", first_marriage: true }],
  ];
  for (const [category, details] of cases) {
    const input = { ...approval, details: { ...none, ...details } };
    assert.notEqual(approvalProblem(category, input), "", category);
    assert.equal(
      approvalProblem(category, { ...input, attachments: 1 }),
      "",
      category,
    );
  }
  assert.equal(approvalProblem("Annual leave", approval), "");
});

test("marriage approval needs completed probation and no earlier approval", () => {
  const input = {
    ...approval,
    details: { ...none, registered_on: "2026-11-01", first_marriage: true },
    attachments: 1,
  };
  assert.equal(approvalProblem("Marriage leave", input), "");
  assert.notEqual(
    approvalProblem("Marriage leave", { ...input, probationCompletedOn: null }),
    "",
  );
  assert.notEqual(
    approvalProblem("Marriage leave", {
      ...input,
      probationCompletedOn: "2026-11-03",
    }),
    "",
    "probation completes after the leave starts",
  );
  assert.notEqual(
    approvalProblem("Marriage leave", { ...input, priorMarriages: 1 }),
    "",
  );
});

test("medical leave is submitted within 48 hours of midnight Malaysia time", () => {
  const start = "2026-11-02";
  for (const [now, ok] of [
    ["2026-10-20T00:00:00Z", true],
    ["2026-11-03T15:59:59Z", true],
    ["2026-11-03T16:00:00Z", true],
    ["2026-11-03T16:00:01Z", false],
  ])
    assert.equal(
      medicalSubmissionProblem(start, new Date(now)) === "",
      ok,
      now,
    );
});

test("paternity approval checks birth date, service and earlier births", () => {
  const details = {
    ...none,
    notified_on: "2026-10-01",
    birth_on: "2026-11-06",
  };
  const input = {
    ...approval,
    details,
    start: "2026-11-06",
    end: "2026-11-12",
    joinDate: "2025-11-06",
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

test("month arithmetic clamps to the end of a shorter month", () => {
  assert.equal(addMonths("2026-11-06", -12), "2025-11-06");
  assert.equal(addMonths("2028-02-29", -12), "2027-02-28");
  assert.equal(addMonths("2026-03-31", -1), "2026-02-28");
  assert.equal(addMonths("2026-05-01", 12), "2027-05-01");
  assert.equal(addMonths("2028-02-29", 12), "2029-02-28");
});
