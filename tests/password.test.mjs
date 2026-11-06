import test from "node:test";
import assert from "node:assert/strict";
import {
  hashPassword,
  verifyPassword,
  rejectAfterHashing,
} from "../app/lib/password.ts";
import { passwordProblem } from "../app/lib/password-rules.ts";

test("a hashed password verifies only with the same password", async () => {
  const stored = await hashPassword("correct horse battery");
  assert.match(stored, /^scrypt\$32768\$8\$1\$/);
  assert.equal(await verifyPassword("correct horse battery", stored), true);
  assert.equal(await verifyPassword("correct horse batterx", stored), false);
});

test("each hash uses a fresh salt", async () => {
  assert.notEqual(
    await hashPassword("same password here"),
    await hashPassword("same password here"),
  );
});

test("malformed stored hashes never verify", async () => {
  for (const stored of ["", "plain-text", "bcrypt$10$abc$def$ghi$jkl"])
    assert.equal(await verifyPassword("anything at all", stored), false);
  assert.equal(await rejectAfterHashing("anything at all"), false);
});

test("passwords must be 12 to 128 characters", () => {
  assert.notEqual(passwordProblem("short"), "");
  assert.notEqual(passwordProblem("x".repeat(129)), "");
  assert.notEqual(passwordProblem(undefined), "");
  assert.equal(passwordProblem("x".repeat(12)), "");
  assert.equal(passwordProblem("x".repeat(128)), "");
});
