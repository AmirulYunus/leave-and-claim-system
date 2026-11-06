import { hasPermission } from "../../lib/permissions";
import { database } from "../../lib/prisma";
import { checkDevelopmentAccess, demoEmail } from "../../lib/demo-access";
import { datesBetween } from "../../lib/time";
import {
  newEmploymentDetails,
  parseEmploymentDetails,
  type EmploymentDetails,
} from "../../lib/employment";
import { Prisma } from "../../generated/prisma/client";

export const runtime = "nodejs";

class ValidationError extends Error {}

const isoDate = (date: Date | null) =>
  date ? date.toISOString().slice(0, 10) : null;
const dbDate = (date: string | null) =>
  date ? new Date(`${date}T00:00:00Z`) : null;

function storedDetails(employee: {
  position: string | null;
  employment_type: string | null;
  employee_level: number | null;
  probation_completed_on: Date | null;
  leaving_date: Date | null;
  is_active: boolean;
  department_id: number | null;
  cost_centre_id: number | null;
}): EmploymentDetails {
  return {
    position: employee.position,
    employment_type: employee.employment_type,
    employee_level: employee.employee_level,
    probation_completed_on: isoDate(employee.probation_completed_on),
    leaving_date: isoDate(employee.leaving_date),
    is_active: employee.is_active,
    department_id: employee.department_id,
    cost_centre_id: employee.cost_centre_id,
  };
}

function employmentData(details: EmploymentDetails) {
  return {
    ...details,
    probation_completed_on: dbDate(details.probation_completed_on),
    leaving_date: dbDate(details.leaving_date),
  };
}

function employmentDetails(
  input: Record<string, unknown>,
  joinDate: string,
  current: EmploymentDetails,
) {
  const details = parseEmploymentDetails(input, joinDate, current);
  if (typeof details === "string") throw new ValidationError(details);
  return details;
}

// Newly chosen classifications must be active; an unchanged one may since have
// been deactivated and is kept.
async function checkClassification(
  tx: Prisma.TransactionClient,
  next: EmploymentDetails,
  current: EmploymentDetails,
) {
  if (next.department_id && next.department_id !== current.department_id) {
    const entry = await tx.departments.findUnique({
      where: { id: next.department_id },
    });
    if (!entry?.is_active)
      throw new ValidationError("Choose an active department.");
  }
  if (next.cost_centre_id && next.cost_centre_id !== current.cost_centre_id) {
    const entry = await tx.cost_centres.findUnique({
      where: { id: next.cost_centre_id },
    });
    if (!entry?.is_active)
      throw new ValidationError("Choose an active cost centre.");
  }
}

function failure(error: unknown) {
  if (error instanceof ValidationError)
    return Response.json(
      { error: error.message },
      {
        status: 400,
      },
    );
  if (
    error instanceof Prisma.PrismaClientKnownRequestError &&
    error.code === "P2002"
  )
    return Response.json(
      { error: "An employee already uses that email." },
      { status: 409 },
    );
  return Response.json(
    {
      error:
        "Could not save employee records. Check the database and migrations.",
    },
    { status: 503 },
  );
}

export async function GET(request: Request) {
  const blocked = checkDevelopmentAccess(request);
  if (blocked) return blocked;
  try {
    const actor = await database().employees.findUnique({
      where: { email: demoEmail(request) },
    });
    if (!actor)
      return Response.json(
        { error: "Forbidden", message: "Forbidden" },
        { status: 403 },
      );
    if (!(await hasPermission(actor.id, "user.view")))
      return Response.json(
        { error: "Forbidden", message: "Forbidden" },
        { status: 403 },
      );
    const employees = await database().employees.findMany({
      orderBy: [{ name: "asc" }, { id: "asc" }],
    });
    return Response.json(
      {
        employees: employees.map((employee) => ({
          ...employee,
          ...storedDetails(employee),
          join_date: isoDate(employee.join_date),
        })),
      },
      { headers: { "Cache-Control": "no-store" } },
    );
  } catch (error) {
    return failure(error);
  }
}

// HR can create Employee and HR profiles. Only Admin can create Admin profiles.
export async function POST(request: Request) {
  const blocked = checkDevelopmentAccess(request);
  if (blocked) return blocked;
  try {
    const input = await request.json();
    const name = typeof input.name === "string" ? input.name.trim() : "";
    const email =
      typeof input.email === "string" ? input.email.trim().toLowerCase() : "";
    const role = input.role ?? "Employee";
    if (
      !name ||
      name.length > 100 ||
      email.length > 254 ||
      !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)
    )
      throw new ValidationError("Enter a name and valid email address.");
    if (
      typeof input.join_date !== "string" ||
      datesBetween(input.join_date, input.join_date).length !== 1
    )
      throw new ValidationError("Enter a valid join date.");
    if (!["Employee", "HR", "Admin"].includes(role))
      throw new ValidationError("Invalid role.");
    const details = employmentDetails(
      input,
      input.join_date,
      newEmploymentDetails,
    );
    const denied = await database().$transaction(async (tx) => {
      const actor = await tx.employees.findUnique({
        where: { email: demoEmail(request) },
      });
      if (!actor)
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      if (!(await hasPermission(actor.id, "user.create", tx)))
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      if (role !== "Employee")
        if (
          !(await hasPermission(
            actor.id,
            role === "HR" ? "user.createHr" : "user.assignRole",
            tx,
          ))
        )
          return Response.json(
            { error: "Forbidden", message: "Forbidden" },
            { status: 403 },
          );
      await checkClassification(tx, details, newEmploymentDetails);
      await tx.employees.create({
        data: {
          name,
          email,
          role,
          join_date: new Date(`${input.join_date}T00:00:00Z`),
          ...employmentData(details),
        },
      });
    });
    if (denied) return denied;
    return Response.json({ saved: true }, { status: 201 });
  } catch (error) {
    return failure(error);
  }
}

// Edit employee details; only Admin can change roles or edit privileged profiles.
export async function PATCH(request: Request) {
  const blocked = checkDevelopmentAccess(request);
  if (blocked) return blocked;
  try {
    const input = await request.json();
    if (!Number.isSafeInteger(input.id))
      throw new ValidationError("Invalid employee.");
    const name = typeof input.name === "string" ? input.name.trim() : "";
    if (
      !name ||
      name.length > 100 ||
      typeof input.join_date !== "string" ||
      datesBetween(input.join_date, input.join_date).length !== 1
    )
      throw new ValidationError("Enter a name and valid join date.");
    if (!["Employee", "HR", "Admin"].includes(input.role))
      throw new ValidationError("Invalid role.");
    const denied = await database().$transaction(async (tx) => {
      // Serialize edits so two changes cannot remove the last active Admin.
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(-1::integer)`;
      const actor = await tx.employees.findUnique({
        where: { email: demoEmail(request) },
      });
      const target = await tx.employees.findUnique({ where: { id: input.id } });
      if (!actor)
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      if (!(await hasPermission(actor.id, "user.update", tx)))
        return Response.json(
          { error: "Forbidden", message: "Forbidden" },
          { status: 403 },
        );
      if (!target) throw new ValidationError("Employee not found.");
      if (target.role !== "Employee")
        if (!(await hasPermission(actor.id, "user.updatePrivileged", tx)))
          return Response.json(
            { error: "Forbidden", message: "Forbidden" },
            { status: 403 },
          );
      if (input.role !== target.role)
        if (!(await hasPermission(actor.id, "user.assignRole", tx)))
          return Response.json(
            { error: "Forbidden", message: "Forbidden" },
            { status: 403 },
          );
      const current = storedDetails(target);
      const details = employmentDetails(input, input.join_date, current);
      if (
        target.role === "Admin" &&
        target.is_active &&
        (input.role !== "Admin" || !details.is_active) &&
        (await tx.employees.count({
          where: { role: "Admin", is_active: true },
        })) <= 1
      )
        throw new ValidationError("Keep at least one active Admin account.");
      await checkClassification(tx, details, current);
      await tx.employees.update({
        where: { id: input.id },
        data: {
          name,
          join_date: new Date(`${input.join_date}T00:00:00Z`),
          role: input.role,
          ...employmentData(details),
        },
      });
    });
    if (denied) return denied;
    return Response.json({ saved: true });
  } catch (error) {
    return failure(error);
  }
}
