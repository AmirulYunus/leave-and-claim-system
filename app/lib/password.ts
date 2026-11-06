import { randomBytes, scrypt, timingSafeEqual } from "node:crypto";

// Stored as scrypt$N$r$p$salt$hash so the cost can be raised later without
// invalidating existing hashes.
const cost = { N: 2 ** 15, r: 8, p: 1, maxmem: 64 * 1024 * 1024 };
const keyLength = 64;

function derive(
  password: string,
  salt: Buffer,
  options: { N: number; r: number; p: number },
) {
  return new Promise<Buffer>((resolve, reject) =>
    scrypt(
      password.normalize("NFKC"),
      salt,
      keyLength,
      { ...options, maxmem: cost.maxmem },
      (error, key) => (error ? reject(error) : resolve(key)),
    ),
  );
}

export async function hashPassword(password: string) {
  const salt = randomBytes(16);
  const key = await derive(password, salt, cost);
  return [
    "scrypt",
    cost.N,
    cost.r,
    cost.p,
    salt.toString("base64"),
    key.toString("base64"),
  ].join("$");
}

export async function verifyPassword(password: string, stored: string) {
  const [scheme, N, r, p, salt, key] = stored.split("$");
  if (scheme !== "scrypt" || !salt || !key) return false;
  const expected = Buffer.from(key, "base64");
  const actual = await derive(password, Buffer.from(salt, "base64"), {
    N: Number(N),
    r: Number(r),
    p: Number(p),
  });
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

// Used when no account matches, so failed sign-ins take the same time.
let dummyHash: Promise<string> | undefined;
export async function rejectAfterHashing(password: string) {
  dummyHash ??= hashPassword(randomBytes(16).toString("hex"));
  await verifyPassword(password, await dummyHash);
  return false;
}
