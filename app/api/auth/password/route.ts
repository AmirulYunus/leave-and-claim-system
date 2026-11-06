import { authenticate } from "../../../lib/auth";
import { database } from "../../../lib/prisma";
import { endOtherSessions } from "../../../lib/session";
import { hashPassword, verifyPassword } from "../../../lib/password";
import { passwordProblem } from "../../../lib/password-rules";

export const runtime = "nodejs";

// Change the signed-in employee's password, including a temporary one.
// Other sessions for the account are signed out.
export async function POST(request: Request) {
  try {
    const employee = await authenticate(request, { allowPasswordChange: true });
    if (employee instanceof Response) return employee;
    const input = await request.json().catch(() => ({}));
    const problem = passwordProblem(input.new_password);
    if (problem) return Response.json({ error: problem }, { status: 400 });
    if (input.new_password === input.current_password)
      return Response.json(
        { error: "Choose a password different from your current one." },
        { status: 400 },
      );
    if (
      typeof input.current_password !== "string" ||
      !employee.password_hash ||
      !(await verifyPassword(input.current_password, employee.password_hash))
    )
      return Response.json(
        { error: "Your current password is incorrect." },
        { status: 400 },
      );
    await database().employees.update({
      where: { id: employee.id },
      data: {
        password_hash: await hashPassword(input.new_password),
        must_change_password: false,
      },
    });
    await endOtherSessions(employee.id, true);
    return Response.json({ saved: true });
  } catch {
    return Response.json(
      { error: "Could not change the password. Check the database." },
      { status: 503 },
    );
  }
}
