import test from "node:test";
import assert from "node:assert/strict";
import {
  roleHasPermission,
  permissions,
} from "../app/lib/permission-policy.ts";

test("employees cannot review requests or manage accounts", () => {
  for (const permission of [
    "leave.approve",
    "claim.approve",
    "user.create",
    "user.createHr",
    "organisation.manage",
    "request.viewAll",
  ])
    assert.equal(roleHasPermission("Employee", permission), false);
  assert.equal(roleHasPermission("Employee", "leave.create"), true);
});
test("HR can review and create Employee and HR profiles but cannot assign roles", () => {
  for (const permission of [
    "leave.approve",
    "claim.approve",
    "user.create",
    "user.createHr",
    "user.update",
    "organisation.manage",
  ])
    assert.equal(roleHasPermission("HR", permission), true);
  for (const permission of ["user.assignRole", "user.updatePrivileged"])
    assert.equal(roleHasPermission("HR", permission), false);
});
test("Admin receives every registered permission; unknown values fail closed", () => {
  for (const permission of permissions)
    assert.equal(roleHasPermission("Admin", permission), true);
  for (const role of [
    "Employee",
    "HR",
    "Admin",
    "Unknown",
    "__proto__",
    "constructor",
  ])
    assert.equal(roleHasPermission(role, "unknown.permission"), false);
  assert.equal(roleHasPermission("Unknown", "leave.approve"), false);
});
