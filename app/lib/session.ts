import { createHash, randomBytes } from "node:crypto";
import { cookies } from "next/headers";
import { database } from "./prisma";

const cookieName = "session";
const sessionHours = 12;
const digest = (token: string) =>
  createHash("sha256").update(token).digest("hex");

export async function createSession(employeeId: number) {
  const token = randomBytes(32).toString("base64url");
  const expires = new Date(Date.now() + sessionHours * 3600_000);
  await database().sessions.create({
    data: {
      token_hash: digest(token),
      employee_id: employeeId,
      expires_at: expires,
    },
  });
  (await cookies()).set(cookieName, token, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    expires,
  });
}

export async function endSession() {
  const store = await cookies();
  const token = store.get(cookieName)?.value;
  if (token)
    await database().sessions.deleteMany({
      where: { token_hash: digest(token) },
    });
  store.delete(cookieName);
}

// Ends every session for an employee, optionally keeping the caller's own.
export async function endOtherSessions(
  employeeId: number,
  keepCurrent = false,
) {
  const token = keepCurrent ? (await cookies()).get(cookieName)?.value : "";
  await database().sessions.deleteMany({
    where: {
      employee_id: employeeId,
      ...(token ? { token_hash: { not: digest(token) } } : {}),
    },
  });
}

// The signed-in, active employee, or null. Expired sessions are removed.
export async function sessionEmployee() {
  const token = (await cookies()).get(cookieName)?.value;
  if (!token) return null;
  const session = await database().sessions.findUnique({
    where: { token_hash: digest(token) },
    include: { employee: true },
  });
  if (!session) return null;
  if (session.expires_at <= new Date()) {
    await database().sessions.deleteMany({
      where: { token_hash: session.token_hash },
    });
    return null;
  }
  return session.employee.is_active ? session.employee : null;
}
