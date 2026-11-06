import { hasPermission } from "../../lib/permissions";
import { database } from "../../lib/prisma";
import { checkDevelopmentAccess, demoEmail } from "../../lib/demo-access";
import { datesBetween } from "../../lib/time";
import { Prisma } from "../../generated/prisma/client";

export const runtime = "nodejs";

class ValidationError extends Error {}

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
          join_date: employee.join_date.toISOString().slice(0, 10),
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
      await tx.employees.create({
        data: {
          name,
          email,
          role,
          join_date: new Date(`${input.join_date}T00:00:00Z`),
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
      // Serialize role edits so two changes cannot remove the last Admin.
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
      if (
        target.role === "Admin" &&
        input.role !== "Admin" &&
        (await tx.employees.count({ where: { role: "Admin" } })) <= 1
      )
        throw new ValidationError("Keep at least one Admin account.");
      await tx.employees.update({
        where: { id: input.id },
        data: {
          name,
          join_date: new Date(`${input.join_date}T00:00:00Z`),
          role: input.role,
        },
      });
    });
    if (denied) return denied;
    return Response.json({ saved: true });
  } catch (error) {
    return failure(error);
  }
}
