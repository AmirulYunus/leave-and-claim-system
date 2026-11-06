import { authenticate, checkOrigin } from "../../../lib/auth";
import { database } from "../../../lib/prisma";
import { createSession, endSession } from "../../../lib/session";
import { rejectAfterHashing, verifyPassword } from "../../../lib/password";
import { passwordLength } from "../../../lib/password-rules";

export const runtime = "nodejs";

const unavailable = () =>
  Response.json(
    { error: "Sign-in is unavailable. Check the database and migrations." },
    { status: 503 },
  );

function profile(employee: {
  id: number;
  name: string;
  role: string;
  must_change_password: boolean;
}) {
  return {
    id: employee.id,
    name: employee.name,
    role: employee.role,
    must_change_password: employee.must_change_password,
  };
}

// The signed-in employee, including one who still has a temporary password.
export async function GET(request: Request) {
  try {
    const employee = await authenticate(request, { allowPasswordChange: true });
    if (employee instanceof Response) return employee;
    return Response.json(profile(employee), {
      headers: { "Cache-Control": "no-store" },
    });
  } catch {
    return unavailable();
  }
}

// Sign in. Unknown, inactive and password-less accounts get the same response
// and timing as a wrong password.
export async function POST(request: Request) {
  const blocked = checkOrigin(request);
  if (blocked) return blocked;
  try {
    const input = await request.json().catch(() => ({}));
    const email =
      typeof input.email === "string" ? input.email.trim().toLowerCase() : "";
    const password =
      typeof input.password === "string" &&
      input.password.length <= passwordLength.max
        ? input.password
        : "";
    const employee =
      email && password
        ? await database().employees.findUnique({ where: { email } })
        : null;
    const valid =
      employee?.password_hash && employee.is_active
        ? await verifyPassword(password, employee.password_hash)
        : await rejectAfterHashing(password);
    if (!employee || !valid)
      return Response.json(
        { error: "Incorrect email or password." },
        { status: 401 },
      );
    await createSession(employee.id);
    return Response.json(profile(employee));
  } catch {
    return unavailable();
  }
}

export async function DELETE(request: Request) {
  const blocked = checkOrigin(request);
  if (blocked) return blocked;
  try {
    await endSession();
    return Response.json({ signedOut: true });
  } catch {
    return unavailable();
  }
}
