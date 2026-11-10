// Cancelling approved leave. The original request stays Approved, and keeps using its balance,
// until HR approves the cancellation; it then becomes Cancelled, which returns
// exactly the hours it used. Cancelling leave that has started is on hold,
// except HR's reasoned correction of approved Emergency leave.

export type CancellationKind = "Cancellation" | "Correction";

export function cancellationKind(input: {
  kind: string;
  status: string;
  category: string;
  employeeId: number;
  dates: string[];
  actorId: number;
  actorCanReview: boolean;
  hasPending: boolean;
  reason: string;
  today: string;
}): { kind: CancellationKind; error?: undefined } | { error: string } {
  if (input.actorId !== input.employeeId && !input.actorCanReview)
    return { error: "Request not found." };
  const reason = input.reason.trim();
  if (!reason || reason.length > 500)
    return { error: "Give a reason of up to 500 characters." };
  if (input.kind !== "Leave" || input.status !== "Approved")
    return { error: "Only approved leave can be cancelled." };
  if (input.hasPending)
    return { error: "A cancellation is already pending for this request." };
  const started = input.dates.some((date) => date <= input.today);
  if (input.actorId === input.employeeId) {
    if (started)
      return {
        error:
          "Cancelling leave that has already started or been taken is on hold; every date must be in the future.",
      };
    return { kind: "Cancellation" };
  }
  if (input.category === "Emergency leave" && started)
    return { kind: "Correction" };
  return {
    error:
      "Only the employee can request cancellation of future leave. HR can correct Emergency leave that has started.",
  };
}

export function cancellationDecisionProblem(input: {
  kind: CancellationKind;
  status: string;
  approving: boolean;
  dates: string[];
  employeeId: number;
  reviewerId: number;
  today: string;
}) {
  if (input.status !== "Pending")
    return "This cancellation has already been decided. Refresh to see its status.";
  if (input.reviewerId === input.employeeId)
    return "You cannot decide the cancellation of your own leave. Another HR member or Admin must review it.";
  if (
    input.approving &&
    input.kind === "Cancellation" &&
    input.dates.some((date) => date <= input.today)
  )
    return "The leave has started, so it can no longer be cancelled. Reject this cancellation.";
  return "";
}
