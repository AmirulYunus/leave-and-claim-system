import { hasPermission } from "../../lib/permissions";
import { database } from "../../lib/prisma";
import { authenticate } from "../../lib/auth";
import { readJsonObject } from "../../lib/json-body";
import { Prisma } from "../../generated/prisma/client";
import type { Permission } from "../../lib/permission-policy";

export const runtime = "nodejs";

// HR-maintained department and cost centre lists used to classify employees.
class ValidationError extends Error {}
const forbidden = () =>
  Response.json({ error: "Forbidden", message: "Forbidden" }, { status: 403 });

function failure(error: unknown) {
  if (error instanceof ValidationError)
    return Response.json({ error: error.message }, { status: 400 });
  if (
    error instanceof Prisma.PrismaClientKnownRequestError &&
    error.code === "P2002"
  )
    return Response.json(
      { error: "That name is already used." },
      { status: 409 },
    );
  return Response.json(
    {
      error:
        "Could not save organisation records. Check the database and migrations.",
    },
    { status: 503 },
  );
}

// The error response for a caller who lacks the permission, if any.
async function deny(request: Request, permission: Permission) {
  const actor = await authenticate(request);
  if (actor instanceof Response) return actor;
  if (!(await hasPermission(actor.id, permission))) return forbidden();
}

function checkType(type: unknown): asserts type is "department" | "costCentre" {
  if (type !== "department" && type !== "costCentre")
    throw new ValidationError("Choose departments or cost centres.");
}

function name(input: { name?: unknown }) {
  const value = typeof input.name === "string" ? input.name.trim() : "";
  if (!value || value.length > 100)
    throw new ValidationError("Enter a name of up to 100 characters.");
  return value;
}

export async function GET(request: Request) {
  try {
    const denied = await deny(request, "user.view");
    if (denied) return denied;
    const order = [{ name: "asc" as const }, { id: "asc" as const }];
    const [departments, costCentres] = await Promise.all([
      database().departments.findMany({ orderBy: order }),
      database().cost_centres.findMany({ orderBy: order }),
    ]);
    return Response.json(
      { departments, costCentres },
      { headers: { "Cache-Control": "no-store" } },
    );
  } catch (error) {
    return failure(error);
  }
}

export async function POST(request: Request) {
  try {
    const denied = await deny(request, "organisation.manage");
    if (denied) return denied;
    const input = await readJsonObject(request);
    if (input instanceof Response) return input;
    checkType(input.type);
    const data = { name: name(input) };
    if (input.type === "department")
      await database().departments.create({ data });
    else await database().cost_centres.create({ data });
    return Response.json({ saved: true }, { status: 201 });
  } catch (error) {
    return failure(error);
  }
}

// Rename or deactivate an entry. Entries are never deleted because employees
// may still reference them.
export async function PATCH(request: Request) {
  try {
    const denied = await deny(request, "organisation.manage");
    if (denied) return denied;
    const input = await readJsonObject(request);
    if (input instanceof Response) return input;
    checkType(input.type);
    if (!Number.isSafeInteger(input.id) || typeof input.is_active !== "boolean")
      throw new ValidationError("Invalid organisation entry.");
    const data = { name: name(input), is_active: input.is_active };
    const where = { id: input.id };
    const updated =
      input.type === "department"
        ? await database().departments.updateMany({ where, data })
        : await database().cost_centres.updateMany({ where, data });
    if (!updated.count) throw new ValidationError("Entry not found.");
    return Response.json({ saved: true });
  } catch (error) {
    return failure(error);
  }
}
