import { sessionEmployee } from "./session";

// Browsers send Origin on cross-site requests; reject them as a CSRF guard in
// addition to the SameSite session cookie. Compare with the Host header because
// request.url may name a different host than the browser used (127.0.0.1 vs
// localhost in development, or the internal address behind a proxy).
export function checkOrigin(request: Request) {
  const origin = request.headers.get("origin");
  if (!origin) return;
  const host =
    request.headers.get("x-forwarded-host") ?? request.headers.get("host");
  let originHost = "";
  try {
    originHost = new URL(origin).host;
  } catch {}
  if (!host || originHost !== host)
    return Response.json({ error: "Invalid request origin." }, { status: 403 });
}

// Resolves the signed-in employee for an API request, or the error response
// to return. Role checks still read the database through hasPermission.
export async function authenticate(
  request: Request,
  { allowPasswordChange = false } = {},
) {
  const blocked = checkOrigin(request);
  if (blocked) return blocked;
  const employee = await sessionEmployee();
  if (!employee)
    return Response.json({ error: "Sign in to continue." }, { status: 401 });
  if (employee.must_change_password && !allowPasswordChange)
    return Response.json(
      {
        error: "Change your temporary password to continue.",
        code: "PASSWORD_CHANGE_REQUIRED",
      },
      { status: 403 },
    );
  return employee;
}
