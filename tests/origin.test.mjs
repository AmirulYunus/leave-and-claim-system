import test from "node:test";
import assert from "node:assert/strict";

process.env.DATABASE_URL ??= "postgresql://leave_app:unused@127.0.0.1:1/unused";

const { checkOrigin } = await import("../app/lib/auth.ts");

const call = (headers) =>
  checkOrigin(new Request("http://localhost:3304/api/workspace", { headers }));

test("a missing Origin is allowed and a host mismatch is rejected", () => {
  assert.equal(call({ host: "localhost:3304" }), undefined);
  assert.equal(
    call({ host: "localhost:3304", origin: "http://localhost:3304" }),
    undefined,
  );
  assert.equal(
    call({ host: "localhost:3304", origin: "http://127.0.0.1:3304" })?.status,
    403,
  );
});

test("Origin is compared with Host, not X-Forwarded-Host", async () => {
  const spoofed = call({
    host: "localhost:3304",
    origin: "https://evil.example",
    "x-forwarded-host": "evil.example",
  });
  assert.equal(spoofed?.status, 403);
  assert.equal((await spoofed.json()).error, "Invalid request origin.");

  assert.equal(
    call({
      host: "localhost:3304",
      origin: "http://localhost:3304",
      "x-forwarded-host": "evil.example",
    }),
    undefined,
  );
});
