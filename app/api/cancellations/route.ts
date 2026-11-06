import { authenticate } from "../../lib/auth";
import { hasPermission } from "../../lib/permissions";
import { database } from "../../lib/prisma";
import { malaysiaDate } from "../../lib/entitlement";
import { closedYearProblem } from "../../lib/annual-ledger";
import { balanceCategory } from "../../lib/leave-types";
import { queueForEmployeeAndHr } from "../../lib/notifications";
import { cancellationDecisionMessages } from "../../lib/notification-messages";
import {
  cancellationDecisionProblem,
  cancellationKind,
  type CancellationKind,
} from "../../lib/cancellation";

export const runtime = "nodejs";

class Refusal extends Error {
  constructor(
    message: string,
    readonly status = 400,
  ) {
    super(message);
  }
}
const failure = (error: unknown) =>
  error instanceof Refusal
    ? Response.json({ error: error.message }, { status: error.status })
    : Response.json(
        { error: "Could not save the cancellation. Check the database." },
        { status: 503 },
      );
const isoDate = (date: Date) => date.toISOString().slice(0, 10);

// Request cancellation of approved leave: the employee for future leave, or HR
// correcting Emergency leave that has started.
export async function POST(request: Request) {
  try {
    const actor = await authenticate(request);
    if (actor instanceof Response) return actor;
    const input = await request.json();
    if (
      !Number.isSafeInteger(input.request_id) ||
      typeof input.reason !== "string"
    )
      throw new Refusal("Choose a request and give a reason.");
    const created = await database().$transaction(async (tx) => {
      const target = await tx.requests.findUnique({
        where: { id: input.request_id },
        select: { employee_id: true },
      });
      if (!target) throw new Refusal("Request not found.", 404);
      // Serialise with leave submissions and decisions for this employee.
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${target.employee_id}::integer)`;
      const leave = await tx.requests.findUniqueOrThrow({
        where: { id: input.request_id },
        include: {
          leave_entries: true,
          cancellations: { where: { status: "Pending" }, select: { id: true } },
        },
      });
      const actorCanReview = await hasPermission(actor.id, "leave.approve", tx);
      const result = cancellationKind({
        kind: leave.kind,
        status: leave.status,
        category: leave.category,
        employeeId: leave.employee_id,
        dates: leave.leave_entries.map((entry) => isoDate(entry.date)),
        actorId: actor.id,
        actorCanReview,
        hasPending: leave.cancellations.length > 0,
        reason: input.reason,
        today: malaysiaDate(),
      });
      if (result.error !== undefined)
        throw new Refusal(
          result.error,
          result.error === "Request not found." ? 404 : 400,
        );
      return tx.cancellations.create({
        data: {
          request_id: leave.id,
          kind: result.kind,
          reason: input.reason.trim(),
          requested_by: actor.id,
        },
      });
    });
    return Response.json({ id: created.id }, { status: 201 });
  } catch (error) {
    return failure(error);
  }
}

// HR decides a pending cancellation. Approval marks the leave Cancelled, which
// returns its hours to the balance.
export async function PATCH(request: Request) {
  try {
    const reviewer = await authenticate(request);
    if (reviewer instanceof Response) return reviewer;
    if (!(await hasPermission(reviewer.id, "leave.approve")))
      throw new Refusal("Forbidden", 403);
    const input = await request.json();
    if (
      !Number.isSafeInteger(input.id) ||
      !["Approved", "Rejected"].includes(input.status)
    )
      throw new Refusal("Invalid cancellation decision.");
    await database().$transaction(async (tx) => {
      const pending = await tx.cancellations.findUnique({
        where: { id: input.id },
        select: { request: { select: { employee_id: true } } },
      });
      if (!pending) throw new Refusal("Cancellation not found.", 404);
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${pending.request.employee_id}::integer)`;
      const cancellation = await tx.cancellations.findUniqueOrThrow({
        where: { id: input.id },
        include: {
          request: { include: { leave_entries: true, employee: true } },
        },
      });
      const approving = input.status === "Approved";
      const problem = cancellationDecisionProblem({
        kind: cancellation.kind as CancellationKind,
        status: cancellation.status,
        approving,
        dates: cancellation.request.leave_entries.map((entry) =>
          isoDate(entry.date),
        ),
        employeeId: cancellation.request.employee_id,
        reviewerId: reviewer.id,
        today: malaysiaDate(),
      });
      if (problem) throw new Refusal(problem);
      if (approving) {
        if (cancellation.request.status !== "Approved")
          throw new Refusal("Only approved leave can be cancelled.");
        const leave = cancellation.request;
        if (
          balanceCategory(leave.category, leave.training_category) ===
          "Annual leave"
        ) {
          const closed = await closedYearProblem(
            tx,
            leave.employee_id,
            leave.leave_entries[0].date.getUTCFullYear(),
          );
          if (closed) throw new Refusal(closed);
        }
        await tx.requests.update({
          where: { id: cancellation.request_id },
          data: { status: "Cancelled" },
        });
      }
      await tx.cancellations.update({
        where: { id: cancellation.id },
        data: {
          status: input.status,
          reviewed_by: reviewer.id,
          reviewed_at: new Date(),
        },
      });
      const leave = cancellation.request;
      await queueForEmployeeAndHr(
        tx,
        leave.employee.email,
        cancellationDecisionMessages({
          category: leave.category,
          detail: leave.detail,
          status: input.status,
          correction: cancellation.kind === "Correction",
          employeeName: leave.employee.name,
          reviewerName: reviewer.name,
        }),
      );
    });
    return Response.json({ saved: true });
  } catch (error) {
    return failure(error);
  }
}
