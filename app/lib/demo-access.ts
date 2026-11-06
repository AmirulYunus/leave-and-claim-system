// Preview identities only. Replace this with server-side sessions when login is added.
export const demoEmails: Record<string, string> = {
  Employee: "amirul@example.test",
  HR: "hr@example.test",
  Admin: "admin@example.test",
};

export function demoEmail(request: Request) {
  return demoEmails[request.headers.get("x-demo-role") ?? "Employee"];
}

export function checkDevelopmentAccess(request: Request) {
  const url = new URL(request.url);
  if (
    process.env.NODE_ENV !== "development" ||
    !["localhost", "127.0.0.1", "[::1]"].includes(url.hostname)
  ) {
    return Response.json(
      {
        error:
          "This unauthenticated demo is available only on localhost in development.",
      },
      { status: 403 },
    );
  }
  const origin = request.headers.get("origin");
  if ((origin && origin !== url.origin) || !demoEmail(request)) {
    return Response.json(
      { error: "Invalid preview request." },
      { status: 403 },
    );
  }
}
