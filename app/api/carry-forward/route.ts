import { authenticate } from "../../lib/auth";
import { readJsonObject } from "../../lib/json-body";
import { hasPermission } from "../../lib/permissions";
import { database } from "../../lib/prisma";
import { malaysiaDate } from "../../lib/entitlement";
import {
  confirmedCarryForward,
  unusedAnnualHours,
} from "../../lib/annual-ledger";
import {
  carryForwardDraft,
  confirmationProblem,
  exceptionProblem,
} from "../../lib/carry-forward";
import { queueForEmployeeAndHr } from "../../lib/notifications";
import { carryForwardMessages } from "../../lib/notification-messages";
import { Prisma } from "../../generated/prisma/client";

export const runtime = "nodejs";

class Refusal extends Error {
  constructor(
    message: string,
    readonly status = 400,
  ) {
    super(message);
  }
}
function failure(error: unknown) {
  if (error instanceof Refusal)
    return Response.json({ error: error.message }, { status: error.status });
  if (
    error instanceof Prisma.PrismaClientKnownRequestError &&
    error.code === "P2002"
  )
    return Response.json(
      { error: "This year has already been recorded for the employee." },
      { status: 409 },
    );
  return Response.json(
    { error: "Could not save the carry-forward. Check the database." },
    { status: 503 },
  );
}

async function reviewer(request: Request) {
  const actor = await authenticate(request);
  if (actor instanceof Response) return actor;
  if (!(await hasPermission(actor.id, "carryForward.manage")))
    return Response.json({ error: "Forbidden" }, { status: 403 });
  return actor;
}

// HR's view of a closing year: each employee's unused Annual Leave, limit and
// carry-forward. In January–March this is last year's final figure; from April
// it is this year's running estimate.
export async function GET(request: Request) {
  try {
    const actor = await reviewer(request);
    if (actor instanceof Response) return actor;
    const today = malaysiaDate();
    const currentYear = Number(today.slice(0, 4));
    const requested = Number(new URL(request.url).searchParams.get("year"));
    const sourceYear = Number.isSafeInteger(requested)
      ? requested
      : today.slice(5) <= "03-31"
        ? currentYear - 1
        : currentYear;
    const rows = await database().$transaction(async (tx) => {
      const employees = await tx.employees.findMany({
        where: { is_active: true },
        orderBy: [{ name: "asc" }, { id: "asc" }],
      });
      return Promise.all(
        employees.map(async (employee) => {
          const unused = await unusedAnnualHours(
            tx,
            employee,
            sourceYear,
            today,
          );
          const exception = await tx.carry_forward_exceptions.findUnique({
            where: {
              employee_id_source_year: {
                employee_id: employee.id,
                source_year: sourceYear,
              },
            },
          });
          const confirmed = await confirmedCarryForward(
            tx,
            employee.id,
            sourceYear,
          );
          return {
            employee_id: employee.id,
            name: employee.name,
            draft: carryForwardDraft(unused, exception?.limit_hours),
            exception: exception && {
              limit_hours: exception.limit_hours,
              document_reference: exception.document_reference,
            },
            confirmed: confirmed && {
              carry_hours: confirmed.carry_hours,
              unused_hours: confirmed.unused_hours,
              reason: confirmed.reason,
              confirmed_at: confirmed.confirmed_at,
            },
          };
        }),
      );
    });
    return Response.json(
      {
        sourceYear,
        provisional: sourceYear >= currentYear,
        canRecordException:
          today >= `${sourceYear}-10-01` && today <= `${sourceYear}-12-15`,
        canConfirm:
          today >= `${sourceYear + 1}-01-01` &&
          today <= `${sourceYear + 1}-03-31`,
        rows,
      },
      { headers: { "Cache-Control": "no-store" } },
    );
  } catch (error) {
    return failure(error);
  }
}

// Record a written higher limit ("exception") or confirm the carry-forward.
export async function POST(request: Request) {
  try {
    const actor = await reviewer(request);
    if (actor instanceof Response) return actor;
    const input = await readJsonObject(request);
    if (input instanceof Response) return input;
    if (
      !["exception", "confirm"].includes(input.action) ||
      !Number.isSafeInteger(input.employee_id) ||
      !Number.isSafeInteger(input.source_year)
    )
      throw new Refusal("Choose an employee, a year and an action.");
    const today = malaysiaDate();
    const reference =
      typeof input.document_reference === "string"
        ? input.document_reference
        : "";
    await database().$transaction(async (tx) => {
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${input.employee_id}::integer)`;
      const employee = await tx.employees.findUnique({
        where: { id: input.employee_id },
      });
      if (!employee) throw new Refusal("Employee not found.", 404);
      if (input.action === "exception") {
        const problem = exceptionProblem({
          sourceYear: input.source_year,
          today,
          limitHours: input.limit_hours,
          documentReference: reference,
        });
        if (problem) throw new Refusal(problem);
        await tx.carry_forward_exceptions.create({
          data: {
            employee_id: employee.id,
            source_year: input.source_year,
            limit_hours: input.limit_hours,
            document_reference: reference.trim(),
            recorded_by: actor.id,
          },
        });
        return;
      }
      if (await confirmedCarryForward(tx, employee.id, input.source_year))
        throw new Refusal(
          "This year's carry-forward is already confirmed.",
          409,
        );
      const exception = await tx.carry_forward_exceptions.findUnique({
        where: {
          employee_id_source_year: {
            employee_id: employee.id,
            source_year: input.source_year,
          },
        },
      });
      const unused = await unusedAnnualHours(
        tx,
        employee,
        input.source_year,
        today,
      );
      const draft = carryForwardDraft(unused, exception?.limit_hours);
      const carryHours = input.carry_hours ?? draft.carry;
      const reason = typeof input.reason === "string" ? input.reason : "";
      const problem = confirmationProblem({
        sourceYear: input.source_year,
        today,
        carryHours,
        draft,
        reason,
        documentReference: reference,
      });
      if (problem) throw new Refusal(problem);
      await tx.carry_forwards.create({
        data: {
          employee_id: employee.id,
          source_year: input.source_year,
          unused_hours: unused,
          limit_hours: draft.limit,
          carry_hours: carryHours,
          reason: reason.trim() || null,
          document_reference: reference.trim() || null,
          confirmed_by: actor.id,
        },
      });
      // Notify only when hours are actually credited.
      if (carryHours > 0)
        await queueForEmployeeAndHr(
          tx,
          employee.email,
          carryForwardMessages({
            employeeName: employee.name,
            hours: carryHours,
            sourceYear: input.source_year,
          }),
        );
    });
    return Response.json({ saved: true }, { status: 201 });
  } catch (error) {
    return failure(error);
  }
}
