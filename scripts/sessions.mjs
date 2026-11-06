// Sign-in helpers for the local check scripts. Load .env.local before use.
export const baseUrl = "http://localhost:3000";
export const demoEmails = {
  Employee: "amirul@example.test",
  HR: "hr@example.test",
  Admin: "admin@example.test",
};

// Returns the Cookie header value for a new session.
export async function signIn(email, password = process.env.SEED_PASSWORD) {
  const response = await fetch(`${baseUrl}/api/auth/session`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });
  if (!response.ok)
    throw new Error(
      `Sign-in failed for ${email} (HTTP ${response.status}). Run npm run db:seed with SEED_PASSWORD set.`,
    );
  return response.headers
    .getSetCookie()
    .map((cookie) => cookie.split(";")[0])
    .join("; ");
}

export async function demoSessions() {
  const entries = await Promise.all(
    Object.entries(demoEmails).map(async ([role, email]) => [
      role,
      await signIn(email),
    ]),
  );
  return Object.fromEntries(entries);
}
