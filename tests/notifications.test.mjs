import test from "node:test";
import assert from "node:assert/strict";
import {
  cancellationDecisionMessages,
  carryForwardMessages,
  carryForwardReminder,
  requestDecisionMessages,
} from "../app/lib/notification-messages.ts";

test("decision emails name the request and the decision, not the reason", () => {
  const messages = requestDecisionMessages({
    kind: "Leave",
    category: "Medical leave",
    detail: "9 Nov 2026 – 10 Nov 2026",
    status: "Approved",
    employeeName: "Sarah Tan",
    reviewerName: "HR",
  });
  assert.equal(messages.employee.subject, "Your leave request was approved");
  assert.match(
    messages.employee.body,
    /Medical leave \(9 Nov 2026 – 10 Nov 2026\) was approved by HR/,
  );
  assert.match(messages.hr.body, /Sarah Tan's Medical leave/);
  const claim = requestDecisionMessages({
    kind: "Claim",
    category: "Travel",
    detail: "Client visit",
    status: "Rejected",
    employeeName: "Daniel Lee",
    reviewerName: "HR",
  });
  assert.equal(claim.employee.subject, "Your claim was rejected");
  assert.equal(claim.hr.subject, "Claim rejected");
});

test("cancellation emails explain what happens to the leave", () => {
  const input = {
    category: "Annual leave",
    detail: "1 Dec 2026",
    correction: false,
    employeeName: "Amirul",
    reviewerName: "HR",
  };
  assert.match(
    cancellationDecisionMessages({ ...input, status: "Approved" }).employee
      .body,
    /hours are returned/,
  );
  assert.match(
    cancellationDecisionMessages({ ...input, status: "Rejected" }).employee
      .body,
    /remains approved/,
  );
  assert.equal(
    cancellationDecisionMessages({
      ...input,
      correction: true,
      status: "Approved",
    }).hr.subject,
    "Leave HR correction approved",
  );
});

test("carry-forward emails give days, hours and the use-by date", () => {
  const messages = carryForwardMessages({
    employeeName: "Amirul",
    hours: 36,
    sourceYear: 2026,
  });
  assert.match(messages.employee.body, /4.5 days \(36 hours\)/);
  assert.match(messages.employee.body, /31 March 2027/);
  assert.match(carryForwardReminder(2026).body, /15 December 2026/);
});
