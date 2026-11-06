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

// Check scripts remove the emails they cause: note the last outbox id before
// the run, then delete newer rows. Local development databases only.
export async function outboxMark(client) {
  const { rows } = await client.query(
    "SELECT coalesce(max(id), 0) AS id FROM notifications",
  );
  return Number(rows[0].id);
}
export function clearOutboxSince(client, id) {
  return client.query("DELETE FROM notifications WHERE id > $1", [id]);
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
