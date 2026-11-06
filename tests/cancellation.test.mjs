import test from "node:test";
import assert from "node:assert/strict";
import {
  cancellationDecisionProblem,
  cancellationKind,
} from "../app/lib/cancellation.ts";

const today = "2026-11-06";
const base = {
  kind: "Leave",
  status: "Approved",
  category: "Annual leave",
  employeeId: 7,
  dates: ["2026-11-09", "2026-11-10"],
  actorId: 7,
  actorCanReview: false,
  hasPending: false,
  reason: "Plans changed",
  today,
};

test("employees cancel their own future approved leave", () => {
  assert.deepEqual(cancellationKind(base), { kind: "Cancellation" });
  for (const change of [
    { reason: "  " },
    { reason: "x".repeat(501) },
    { status: "Pending" },
    { kind: "Claim" },
    { hasPending: true },
    { dates: ["2026-11-06", "2026-11-09"] },
    { dates: ["2026-11-05"] },
  ])
    assert.ok(
      cancellationKind({ ...base, ...change }).error,
      JSON.stringify(change),
    );
});

test("HR corrects started emergency leave but not other leave", () => {
  const hr = { ...base, actorId: 3, actorCanReview: true };
  assert.deepEqual(
    cancellationKind({
      ...hr,
      category: "Emergency leave",
      dates: ["2026-11-02"],
    }),
    { kind: "Correction" },
  );
  assert.ok(cancellationKind(hr).error, "future annual leave");
  assert.ok(
    cancellationKind({ ...hr, dates: ["2026-11-02"] }).error,
    "started annual leave",
  );
  assert.ok(
    cancellationKind({ ...base, actorId: 9 }).error,
    "another employee",
  );
});

test("decisions are pending, not self-reviewed, and still in the future", () => {
  const decision = {
    kind: "Cancellation",
    status: "Pending",
    approving: true,
    dates: ["2026-11-09"],
    employeeId: 7,
    reviewerId: 3,
    today,
  };
  assert.equal(cancellationDecisionProblem(decision), "");
  assert.notEqual(
    cancellationDecisionProblem({ ...decision, status: "Approved" }),
    "",
  );
  assert.notEqual(
    cancellationDecisionProblem({ ...decision, reviewerId: 7 }),
    "",
  );
  assert.notEqual(
    cancellationDecisionProblem({ ...decision, today: "2026-11-09" }),
    "",
    "leave started while pending",
  );
  assert.equal(
    cancellationDecisionProblem({
      ...decision,
      today: "2026-11-09",
      approving: false,
    }),
    "",
  );
  assert.equal(
    cancellationDecisionProblem({
      ...decision,
      kind: "Correction",
      today: "2026-11-09",
    }),
    "",
  );
});
