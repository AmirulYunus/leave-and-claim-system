import test from "node:test";
import assert from "node:assert/strict";
import {
  carryAllocation,
  carryForwardDraft,
  confirmationProblem,
  exceptionProblem,
} from "../app/lib/carry-forward.ts";

test("carry-forward is the unused leave up to the limit", () => {
  assert.deepEqual(carryForwardDraft(32), {
    unusedHours: 32,
    limit: 40,
    carry: 32,
    remainder: 0,
  });
  assert.deepEqual(carryForwardDraft(64), {
    unusedHours: 64,
    limit: 40,
    carry: 40,
    remainder: 24,
  });
  assert.equal(carryForwardDraft(64, 56).carry, 56);
  assert.equal(carryForwardDraft(0).carry, 0);
});

test("written exceptions are recorded 1 October to 15 December", () => {
  const input = {
    sourceYear: 2026,
    today: "2026-11-06",
    limitHours: 56,
    documentReference: "Memo 2026/14",
  };
  assert.equal(exceptionProblem(input), "");
  for (const change of [
    { today: "2026-09-30" },
    { today: "2026-12-16" },
    { today: "2027-01-02" },
    { limitHours: 40 },
    { limitHours: 42 },
    { limitHours: 1004 },
    { documentReference: " " },
    { documentReference: "x".repeat(201) },
  ])
    assert.notEqual(
      exceptionProblem({ ...input, ...change }),
      "",
      JSON.stringify(change),
    );
  assert.equal(exceptionProblem({ ...input, today: "2026-10-01" }), "");
  assert.equal(exceptionProblem({ ...input, today: "2026-12-15" }), "");
});

test("HR confirms 1 January to 31 March and may only reduce, with reasons", () => {
  const input = {
    sourceYear: 2026,
    today: "2027-01-15",
    carryHours: 40,
    draft: { carry: 40 },
    reason: "",
    documentReference: "",
  };
  assert.equal(confirmationProblem(input), "");
  assert.equal(confirmationProblem({ ...input, today: "2027-03-31" }), "");
  for (const change of [
    { today: "2026-12-31" },
    { today: "2027-04-01" },
    { carryHours: 44 },
    { carryHours: 30 },
    { carryHours: -4 },
    { carryHours: 32 },
    { carryHours: 32, reason: "Agreed" },
  ])
    assert.notEqual(
      confirmationProblem({ ...input, ...change }),
      "",
      JSON.stringify(change),
    );
  assert.equal(
    confirmationProblem({
      ...input,
      carryHours: 32,
      reason: "Agreed with employee",
      documentReference: "Email 12 Jan",
    }),
    "",
  );
});

test("carry-forward is spent first on dates up to 31 March", () => {
  const entries = [
    { date: "2027-03-30", hours: 8 },
    { date: "2027-03-31", hours: 8 },
    { date: "2027-04-01", hours: 8 },
  ];
  assert.equal(carryAllocation(entries, 40, "2027-03-31"), 16);
  assert.equal(carryAllocation(entries, 12, "2027-03-31"), 12);
  assert.equal(carryAllocation(entries, 0, "2027-03-31"), 0);
  assert.equal(
    carryAllocation([{ date: "2027-05-03", hours: 8 }], 40, "2027-03-31"),
    0,
  );
});
