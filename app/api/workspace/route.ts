import { hasPermission } from "../../lib/permissions";
import { authenticate } from "../../lib/auth";
import { database } from "../../lib/prisma";
import { datesBetween, validateLeave, type LeaveEntry } from "../../lib/time";
import {
  entitlementRates,
  leaveAllowanceHours,
  malaysiaDate,
} from "../../lib/entitlement";
import {
  approvalProblem,
  balanceCategory,
  isFixedLength,
  leaveCategories,
  medicalSubmissionProblem,
  scheduleProblem,
  submissionProblem,
  type LeaveDetails,
} from "../../lib/leave-types";
import {
  approvedAnnualUse,
  closedYearProblem,
  confirmedCarryForward,
  employmentOf,
  unusedAnnualHours,
} from "../../lib/annual-ledger";
import { carryAllocation, carryUseBy } from "../../lib/carry-forward";
import { queueForEmployeeAndHr } from "../../lib/notifications";
import { requestDecisionMessages } from "../../lib/notification-messages";
import type { Prisma } from "../../generated/prisma/client";
export const runtime = "nodejs";

// Shared helpers stay in this file so each handler is easy to follow.
class ValidationError extends Error {}

function errorResponse(error: unknown) {
  return Response.json(
    {
      error:
        error instanceof ValidationError
          ? error.message
          : "Could not access the database. Check that PostgreSQL is running and migrations have been applied.",
    },
    {
      status: error instanceof ValidationError ? 400 : 503,
    },
  );
}

type Transaction = Prisma.TransactionClient;
const isoDate = (date: Date) => date.toISOString().slice(0, 10);
const dbDate = (date: string) => new Date(`${date}T00:00:00Z`);

// Re-read the signed-in employee inside the transaction.
function getEmployeeProfile(tx: Transaction, id: number) {
  return tx.employees.findUniqueOrThrow({ where: { id } });
}
type EmployeeRow = Awaited<ReturnType<typeof getEmployeeProfile>>;

const employment = employmentOf;

// This year's usable hours per entitled category, for the dashboard.
async function entitlementSummary(tx: Transaction, employee: EmployeeRow) {
  const today = malaysiaDate();
  const year = Number(today.slice(0, 4));
  const details = employment(employee);
  const rates = entitlementRates(details, today);
  const summary = (category: string, rateDays: number | null) => {
    const allowance = leaveAllowanceHours(details, category, year, today);
    return {
      hours: allowance.hours ?? null,
      reason: allowance.reason ?? "",
      rateDays,
    };
  };
  const credit = await confirmedCarryForward(tx, employee.id, year - 1);
  const use = await approvedAnnualUse(tx, employee.id, year);
  const useBy = carryUseBy(year);
  return {
    year,
    asOf: today,
    annual: summary("Annual leave", rates.annual.days),
    medical: summary("Medical leave", rates.sick.days),
    hospitalisation: summary(
      "Hospitalisation leave",
      rates.hospitalisation.days,
    ),
    // Carry-forward from last year, usable for dates up to 31 March.
    carryForward: {
      hours: credit?.carry_hours ?? 0,
      remaining:
        today <= useBy
          ? Math.max(0, (credit?.carry_hours ?? 0) - use.carry)
          : 0,
      used: use.carry,
      useBy,
    },
  };
}
async function getLeaveRecords(
  tx: Transaction,
  employeeId: number,
  excludeId?: number,
) {
  const entries = await tx.leave_entries.findMany({
    where: {
      requests: {
        employee_id: employeeId,
        status: { notIn: ["Rejected", "Cancelled"] },
        ...(excludeId ? { id: { not: excludeId } } : {}),
      },
    },
    include: {
      requests: {
        select: { category: true, status: true, training_category: true },
      },
    },
  });
  return entries.map((entry) => ({
    date: isoDate(entry.date),
    hours: Number(entry.hours),
    category: entry.requests.category,
    status: entry.requests.status,
    balance: balanceCategory(
      entry.requests.category,
      entry.requests.training_category,
    ),
  }));
}

const optionalDate = (value: unknown) => {
  if (value === undefined || value === null || value === "") return null;
  if (typeof value !== "string" || datesBetween(value, value).length !== 1)
    throw new ValidationError("Enter valid birth and notice dates.");
  return value;
};
// Only the details that apply to the category are kept.
function leaveDetails(category: string, input: Record<string, unknown>) {
  const birth = ["Maternity leave", "Paternity leave"].includes(category);
  return {
    expected_birth_on: birth ? optionalDate(input.expected_birth_on) : null,
    birth_on:
      category === "Paternity leave" ? optionalDate(input.birth_on) : null,
    notified_on: birth ? optionalDate(input.notified_on) : null,
    training_category:
      category === "Training leave" &&
      typeof input.training_category === "string"
        ? input.training_category
        : null,
    registered_on:
      category === "Marriage leave" ? optionalDate(input.registered_on) : null,
    first_marriage:
      category === "Marriage leave" ? input.first_marriage === true : null,
    relationship:
      category === "Compassionate leave" &&
      typeof input.relationship === "string"
        ? input.relationship
        : null,
  };
}
const optionalIso = (date: Date | null) => (date ? isoDate(date) : null);
const optionalDb = (date: string | null) => (date ? dbDate(date) : null);
const storedDetails = (request: {
  expected_birth_on: Date | null;
  birth_on: Date | null;
  notified_on: Date | null;
  training_category: string | null;
  registered_on: Date | null;
  first_marriage: boolean | null;
  relationship: string | null;
}): LeaveDetails => ({
  expected_birth_on: optionalIso(request.expected_birth_on),
  birth_on: optionalIso(request.birth_on),
  notified_on: optionalIso(request.notified_on),
  training_category: request.training_category,
  registered_on: optionalIso(request.registered_on),
  first_marriage: request.first_marriage,
  relationship: request.relationship,
});
const detailData = (details: LeaveDetails) => ({
  ...details,
  expected_birth_on: optionalDb(details.expected_birth_on),
  birth_on: optionalDb(details.birth_on),
  notified_on: optionalDb(details.notified_on),
  registered_on: optionalDb(details.registered_on),
});

// New requests that use a yearly balance (or are recorded per weekday) must
// fall in the current Malaysia calendar year; a pending request from a closed
// year can still be decided. Maternity and paternity may cross a year end.
async function validateLeaveEntries(
  tx: Transaction,
  employee: EmployeeRow,
  category: string,
  entries: LeaveEntry[],
  details: LeaveDetails,
  {
    excludeId,
    submitting,
    submittedAt = new Date(),
  }: { excludeId?: number; submitting: boolean; submittedAt?: Date },
) {
  const fixed = isFixedLength(category);
  if (
    !entries.length ||
    entries.length > 366 ||
    new Set(entries.map((entry) => entry.date)).size !== entries.length
  )
    throw new ValidationError("Choose unique eligible dates.");
  if (
    entries.some((entry) => datesBetween(entry.date, entry.date).length !== 1)
  )
    throw new ValidationError("Use valid calendar dates.");
  const schedule = scheduleProblem(category, entries);
  if (schedule) throw new ValidationError(schedule);
  const sorted = entries.map((entry) => entry.date).sort();
  if (submitting) {
    const problem = submissionProblem(
      category,
      details,
      sorted[0],
      sorted.at(-1)!,
    );
    if (problem) throw new ValidationError(problem);
  }
  if (category === "Medical leave") {
    const late = medicalSubmissionProblem(sorted[0], submittedAt);
    if (late) throw new ValidationError(late);
  }
  const today = malaysiaDate();
  const year = Number(entries[0].date.slice(0, 4));
  if (!fixed) {
    if (new Set(entries.map((entry) => entry.date.slice(0, 4))).size !== 1)
      throw new ValidationError(
        "Submit separate requests for each calendar year.",
      );
    if (submitting && year !== Number(today.slice(0, 4)))
      throw new ValidationError(
        `Leave can be requested for ${today.slice(0, 4)} dates only.`,
      );
  }
  const balance = balanceCategory(category, details.training_category);
  const records = await getLeaveRecords(tx, employee.id, excludeId);
  let available = Infinity;
  let carryHours = 0;
  if (balance) {
    if (!submitting && balance === "Annual leave") {
      const closed = await closedYearProblem(tx, employee.id, year);
      if (closed) throw new ValidationError(closed);
    }
    const allowance = leaveAllowanceHours(
      employment(employee),
      balance,
      year,
      today,
    );
    if (allowance.hours === undefined)
      throw new ValidationError(
        `${category} is unavailable: ${allowance.reason}`,
      );
    // An approval counts approved leave only; a new request also reserves the
    // employee's pending requests.
    const inYear = records.filter(
      (record) =>
        record.balance === balance && record.date.startsWith(`${year}-`),
    );
    const counted = submitting
      ? inYear
      : inYear.filter((record) => record.status === "Approved");
    available =
      allowance.hours - counted.reduce((sum, record) => sum + record.hours, 0);
    if (balance === "Annual leave") {
      const useBy = carryUseBy(year);
      const credit = await confirmedCarryForward(tx, employee.id, year - 1);
      const priorUnusedHours = async () => {
        const unused = await unusedAnnualHours(tx, employee, year - 1, today);
        return "hours" in unused ? unused.hours : 0;
      };
      // Wait for HR's carry-forward decision while it can still be made
      // (until 31 March); afterwards the leave is paid from earned leave.
      if (
        !submitting &&
        !credit &&
        today <= useBy &&
        entries.some((entry) => entry.date <= useBy) &&
        (await priorUnusedHours()) > 0
      )
        throw new ValidationError(
          `HR must confirm the ${year - 1} Annual Leave carry-forward before January–March ${year} Annual Leave can be approved.`,
        );
      // Carry-forward is spent first on dates up to 31 March.
      const use = await approvedAnnualUse(tx, employee.id, year, excludeId);
      const remaining = (credit?.carry_hours ?? 0) - use.carry;
      const pending = submitting
        ? inYear.filter((record) => record.status === "Pending")
        : [];
      carryHours = carryAllocation(entries, remaining, useBy);
      available +=
        use.carry + carryAllocation([...pending, ...entries], remaining, useBy);
    }
  }
  const error = validateLeave(entries, records, available);
  if (error) throw new ValidationError(error);
  const worked = await tx.timesheet_entries.findMany({
    where: { employee_id: employee.id },
  });
  if (
    entries.some(
      (entry) =>
        entry.hours +
          records
            .filter((record) => record.date === entry.date)
            .reduce((sum, record) => sum + record.hours, 0) +
          Number(
            worked.find((day) => isoDate(day.date) === entry.date)?.hours ?? 0,
          ) >
        8,
    )
  )
    throw new ValidationError(
      "Worked hours plus requested leave exceed 8 hours. Update the timesheet first.",
    );
  return { carryHours };
}

// Load the requests and worked hours displayed in the workspace.
export async function GET(request: Request) {
  try {
    const actor = await authenticate(request);
    if (actor instanceof Response) return actor;
    const data = await database().$transaction(
      async (tx) => {
        const employee = await getEmployeeProfile(tx, actor.id);
        if (!(await hasPermission(employee.id, "request.view", tx)))
          return Response.json(
            { error: "Forbidden", message: "Forbidden" },
            { status: 403 },
          );
        const requests = await tx.requests.findMany({
          where: (await hasPermission(employee.id, "request.viewAll", tx))
            ? {}
            : { employee_id: employee.id },
          include: {
            employee: true,
            claims: true,
            leave_entries: { orderBy: { date: "asc" } },
            attachments: {
              select: { id: true, file_name: true, size_bytes: true },
              orderBy: { id: "asc" },
            },
            cancellations: {
              select: { id: true, kind: true, status: true, reason: true },
              orderBy: { id: "asc" },
            },
          },
          orderBy: [{ created_at: "desc" }, { id: "desc" }],
        });
        const times = await tx.timesheet_entries.findMany({
          where: { employee_id: employee.id },
        });
        return {
          employee: {
            id: employee.id,
            name: employee.name,
            role: employee.role,
            join_date: isoDate(employee.join_date),
          },
          entitlements: await entitlementSummary(tx, employee),
          worked: Object.fromEntries(
            times.map((day) => [isoDate(day.date), day.hours.toString()]),
          ),
          requests: requests.map((item) => ({
            id: item.id,
            person: item.employee.name,
            employee_id: item.employee_id,
            kind: item.kind,
            category: item.category,
            detail: item.detail,
            status: item.status,
            note: item.note,
            value:
              item.kind === "Claim"
                ? Number(item.claims?.amount ?? 0)
                : item.leave_entries.reduce(
                    (sum, entry) => sum + Number(entry.hours),
                    0,
                  ) / 8,
            entries: item.leave_entries.map((entry) => ({
              date: isoDate(entry.date),
              hours: Number(entry.hours),
            })),
            details: storedDetails(item),
            attachments: item.attachments,
            cancellations: item.cancellations,
          })),
        };
      },
      { isolationLevel: "RepeatableRead" },
    );
    if (data instanceof Response) return data;
    return Response.json(data, { headers: { "Cache-Control": "no-store" } });
  } catch (error) {
    return errorResponse(error);
  }
}

// Validate and save a new leave request or expense claim.
export async function POST(request: Request) {
  try {
    const actor = await authenticate(request);
    if (actor instanceof Response) return actor;
    const input = await request.json();
    if (
      !["Leave", "Claim"].includes(input.kind) ||
      typeof input.note !== "string" ||
      !input.note.trim() ||
      input.note.length > 500 ||
      typeof input.detail !== "string" ||
      !input.detail.trim() ||
      input.detail.length > 200
    )
      throw new ValidationError(
        "Enter a valid request description and reason.",
      );
    const categories: readonly string[] =
      input.kind === "Leave"
        ? leaveCategories
        : ["Travel", "Meals", "Office supplies", "Other"];
    if (!categories.includes(input.category))
      throw new ValidationError("Invalid category.");
    const details =
      input.kind === "Leave" ? leaveDetails(input.category, input) : null;
    if (
      input.kind === "Claim" &&
      (typeof input.value !== "number" ||
        !Number.isFinite(input.value) ||
        input.value <= 0 ||
        input.value > 100000 ||
        Math.abs(input.value * 100 - Math.round(input.value * 100)) > 0.000001)
    )
      throw new ValidationError(
        "Enter a claim amount from RM 0.01 to RM 100,000 with at most two decimal places.",
      );
    const created = await database().$transaction(async (tx) => {
      const employee = await getEmployeeProfile(tx, actor.id);
      if (
        !(await hasPermission(
          employee.id,
          input.kind === "Leave" ? "leave.create" : "claim.create",
          tx,
        ))
      )
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      // Serialize changes for this employee. PostgreSQL advisory locks require SQL.
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${employee.id}::integer)`;
      if (input.kind === "Leave") {
        if (
          !Array.isArray(input.entries) ||
          input.entries.some(
            (entry: LeaveEntry) =>
              !entry ||
              typeof entry.date !== "string" ||
              typeof entry.hours !== "number",
          )
        )
          throw new ValidationError("Invalid leave hours.");
        await validateLeaveEntries(
          tx,
          employee,
          input.category,
          input.entries,
          details!,
          { submitting: true },
        );
      }
      return tx.requests.create({
        data: {
          employee_id: employee.id,
          kind: input.kind,
          category: input.category,
          detail: input.detail.trim(),
          note: input.note.trim(),
          ...(details ? detailData(details) : {}),
          ...(input.kind === "Leave"
            ? {
                leave_entries: {
                  create: input.entries.map((entry: LeaveEntry) => ({
                    date: dbDate(entry.date),
                    hours: entry.hours,
                  })),
                },
              }
            : { claims: { create: { amount: input.value } } }),
        },
      });
    });
    if (created instanceof Response) return created;
    return Response.json({ saved: true, id: created.id }, { status: 201 });
  } catch (error) {
    return errorResponse(error);
  }
}

// Review a pending request and record the HR decision.
export async function PATCH(request: Request) {
  try {
    const reviewer = await authenticate(request);
    if (reviewer instanceof Response) return reviewer;
    // Reject non-reviewers before any lookup so request IDs are not disclosed.
    if (!(
      (await hasPermission(reviewer.id, "leave.approve")) ||
      (await hasPermission(reviewer.id, "claim.approve"))
    ))
      return Response.json(
        { error: "Forbidden", message: "Forbidden" },
        { status: 403 },
      );
    const input = await request.json();
    if (
      !Number.isSafeInteger(input.id) ||
      !["Approved", "Rejected"].includes(input.status)
    )
      throw new ValidationError("Invalid review decision.");
    const denied = await database().$transaction(async (tx) => {
      const initial = await tx.requests.findUnique({ where: { id: input.id } });
      if (!initial) throw new ValidationError("Request not found.");
      if (
        !(await hasPermission(
          reviewer.id,
          initial.kind === "Leave" ? "leave.approve" : "claim.approve",
          tx,
        ))
      )
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${initial.employee_id}::integer)`;
      const target = await tx.requests.findUnique({
        where: { id: input.id },
        include: {
          employee: true,
          leave_entries: true,
          _count: { select: { attachments: true } },
        },
      });
      if (!target || target.status !== "Pending")
        throw new ValidationError(
          "This request has already been reviewed. Refresh to see its status.",
        );
      if (target.employee_id === reviewer.id)
        return Response.json(
          {
            error:
              "You cannot decide your own request. Another HR member or Admin must review it.",
          },
          { status: 403 },
        );
      const details = storedDetails(target);
      // HR records the verified birth date when approving paternity leave
      // requested before the birth.
      if (
        target.category === "Paternity leave" &&
        input.status === "Approved" &&
        !details.birth_on
      )
        details.birth_on = optionalDate(input.birth_on);
      let carryHours = 0;
      if (target.kind === "Leave" && input.status === "Approved") {
        const entries = target.leave_entries.map((entry) => ({
          date: isoDate(entry.date),
          hours: Number(entry.hours),
        }));
        const priorBirths =
          target.category === "Paternity leave"
            ? (
                await tx.requests.findMany({
                  // A cancellation does not undo a birth.
                  where: {
                    employee_id: target.employee_id,
                    category: "Paternity leave",
                    status: { in: ["Approved", "Cancelled"] },
                    id: { not: target.id },
                  },
                  select: { birth_on: true },
                })
              ).map((prior) =>
                prior.birth_on ? isoDate(prior.birth_on) : null,
              )
            : [];
        const priorMarriages =
          target.category === "Marriage leave"
            ? await tx.requests.count({
                where: {
                  employee_id: target.employee_id,
                  category: "Marriage leave",
                  status: "Approved",
                  id: { not: target.id },
                },
              })
            : 0;
        const dates = entries.map((entry) => entry.date).sort();
        const problem = approvalProblem(target.category, {
          details,
          start: dates[0],
          end: dates.at(-1)!,
          joinDate: isoDate(target.employee.join_date),
          probationCompletedOn: target.employee.probation_completed_on
            ? isoDate(target.employee.probation_completed_on)
            : null,
          attachments: target._count.attachments,
          priorBirths,
          priorMarriages,
        });
        if (problem) throw new ValidationError(problem);
        ({ carryHours } = await validateLeaveEntries(
          tx,
          target.employee,
          target.category,
          entries,
          details,
          {
            excludeId: input.id,
            submitting: false,
            submittedAt: target.created_at,
          },
        ));
      }
      await tx.requests.update({
        where: { id: input.id },
        data: {
          status: input.status,
          reviewed_by: reviewer.id,
          reviewed_at: new Date(),
          carry_forward_hours: carryHours,
          ...(details.birth_on && !target.birth_on
            ? { birth_on: dbDate(details.birth_on) }
            : {}),
        },
      });
      await queueForEmployeeAndHr(
        tx,
        target.employee.email,
        requestDecisionMessages({
          kind: target.kind,
          category: target.category,
          detail: target.detail,
          status: input.status,
          employeeName: target.employee.name,
          reviewerName: reviewer.name,
        }),
      );
    });
    if (denied) return denied;
    return Response.json({ saved: true });
  } catch (error) {
    return errorResponse(error);
  }
}

// Validate and save the edited monthly timesheet.
export async function PUT(request: Request) {
  try {
    const actor = await authenticate(request);
    if (actor instanceof Response) return actor;
    const input = await request.json();
    if (
      typeof input.month !== "string" ||
      !/^\d{4}-(0[1-9]|1[0-2])$/.test(input.month) ||
      !input.worked ||
      typeof input.worked !== "object" ||
      Array.isArray(input.worked)
    )
      throw new ValidationError("Invalid timesheet month.");
    const entries = Object.entries(input.worked) as [string, string][];
    for (const [date, raw] of entries) {
      if (
        !date.startsWith(input.month + "-") ||
        datesBetween(date, date).length !== 1 ||
        typeof raw !== "string" ||
        (raw !== "" &&
          (!Number.isFinite(Number(raw)) ||
            Number(raw) < 0 ||
            Number(raw) > 24))
      )
        throw new ValidationError(
          "Enter valid dates and worked hours from 0 to 24.",
        );
    }
    const denied = await database().$transaction(async (tx) => {
      const employee = await getEmployeeProfile(tx, actor.id);
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${employee.id}::integer)`;
      if (!(await hasPermission(employee.id, "timesheet.updateOwn", tx)))
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      const records = await getLeaveRecords(tx, employee.id);
      for (const [date, raw] of entries) {
        const leave = records
          .filter(
            (record) => record.date === date && record.status === "Approved",
          )
          .reduce((sum, record) => sum + record.hours, 0);
        if (leave > 0 && Number(raw) + leave > 8)
          throw new ValidationError(
            "Worked hours plus approved leave cannot exceed 8 hours.",
          );
        if (raw === "")
          await tx.timesheet_entries.deleteMany({
            where: { employee_id: employee.id, date: dbDate(date) },
          });
        else
          await tx.timesheet_entries.upsert({
            where: {
              employee_id_date: {
                employee_id: employee.id,
                date: dbDate(date),
              },
            },
            create: {
              employee_id: employee.id,
              date: dbDate(date),
              hours: Number(raw),
            },
            update: { hours: Number(raw), updated_at: new Date() },
          });
      }
    });
    if (denied) return denied;
    return Response.json({ saved: true });
  } catch (error) {
    return errorResponse(error);
  }
}
