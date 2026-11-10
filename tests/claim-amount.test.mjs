import test from "node:test";
import assert from "node:assert/strict";
import { claimAmountProblem } from "../app/lib/claim-amount.ts";

const message =
  "Enter a claim amount from RM 0.01 to RM 100,000 with at most two decimal places.";

test("a claim is RM 0.01 to RM 100,000 with at most two decimal places", () => {
  for (const value of [0.01, 100000, 0.1 + 0.2])
    assert.equal(claimAmountProblem(value), "", String(value));
  for (const value of [
    1e-8,
    Number.MIN_VALUE,
    0.001,
    100000.01,
    1.005,
    NaN,
    Infinity,
    "12.3",
  ])
    assert.equal(claimAmountProblem(value), message, String(value));
});
