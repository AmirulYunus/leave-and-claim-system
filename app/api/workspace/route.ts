import { hasPermission } from "../../lib/permissions";
import { authenticate } from "../../lib/auth";
import { database } from "../../lib/prisma";
import {
  annualAllowance,
  sickAllowance,
  datesBetween,
  isWeekday,
  validateLeave,
  type LeaveEntry,
} from "../../lib/time";
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
async function getLeaveRecords(
  tx: Transaction,
  employeeId: number,
  excludeId?: number,
) {
  const entries = await tx.leave_entries.findMany({
    where: {
      requests: {
        employee_id: employeeId,
        status: { not: "Rejected" },
        ...(excludeId ? { id: { not: excludeId } } : {}),
      },
    },
    include: { requests: { select: { category: true, status: true } } },
  });
  return entries.map((entry) => ({
    date: isoDate(entry.date),
    hours: Number(entry.hours),
    category: entry.requests.category,
    status: entry.requests.status,
  }));
}
async function validateLeaveEntries(
  tx: Transaction,
  employee: { id: number; join_date: Date },
  category: string,
  entries: LeaveEntry[],
  excludeId?: number,
) {
  const medical = ["Medical leave", "Hospitalisation leave"].includes(category);
  if (
    !entries.length ||
    entries.length > 366 ||
    new Set(entries.map((entry) => entry.date)).size !== entries.length
  )
    throw new ValidationError("Choose unique eligible dates.");
  if (
    entries.some(
      (entry) =>
        datesBetween(entry.date, entry.date).length !== 1 ||
        !entry.date.startsWith("2026-") ||
        (!medical && !isWeekday(entry.date)) ||
        (medical && entry.hours !== 8),
    )
  )
    throw new ValidationError(
      "Use eligible 2026 dates. Medical leave is recorded in full days.",
    );
  const records = await getLeaveRecords(tx, employee.id, excludeId);
  let allowance = Infinity;
  if (category === "Annual leave")
    allowance = annualAllowance(isoDate(employee.join_date), "2026-10-06");
  else if (category === "Medical leave")
    allowance = sickAllowance(isoDate(employee.join_date), "2026-10-06");
  else if (category === "Hospitalisation leave") allowance = 60;
  const used = records
    .filter(
      (record) =>
        record.category === category && record.date.startsWith("2026-"),
    )
    .reduce((sum, record) => sum + record.hours, 0);
  const error = validateLeave(entries, records, allowance * 8 - used);
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
    const categories =
      input.kind === "Leave"
        ? [
            "Annual leave",
            "Medical leave",
            "Hospitalisation leave",
            "Unpaid leave",
          ]
        : ["Travel", "Meals", "Office supplies", "Other"];
    if (!categories.includes(input.category))
      throw new ValidationError("Invalid category.");
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
    const denied = await database().$transaction(async (tx) => {
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
        await validateLeaveEntries(tx, employee, input.category, input.entries);
      }
      await tx.requests.create({
        data: {
          employee_id: employee.id,
          kind: input.kind,
          category: input.category,
          detail: input.detail.trim(),
          note: input.note.trim(),
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
    if (denied) return denied;
    return Response.json({ saved: true }, { status: 201 });
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
        include: { employee: true, leave_entries: true },
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
      if (
        ["Medical leave", "Hospitalisation leave"].includes(target.category) &&
        input.medicalVerified !== true
      )
        throw new ValidationError(
          "HR must verify medical certification and statutory eligibility.",
        );
      if (target.kind === "Leave" && input.status === "Approved")
        await validateLeaveEntries(
          tx,
          target.employee,
          target.category,
          target.leave_entries.map((entry) => ({
            date: isoDate(entry.date),
            hours: Number(entry.hours),
          })),
          input.id,
        );
      await tx.requests.update({
        where: { id: input.id },
        data: {
          status: input.status,
          reviewed_by: reviewer.id,
          reviewed_at: new Date(),
        },
      });
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
