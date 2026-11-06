// Central permission catalogue. Unknown roles and permissions are denied.
export const permissions = [
  "user.view",
  "user.create",
  "user.createHr",
  "user.update",
  "user.assignRole",
  "user.updatePrivileged",
  "organisation.manage",
  "carryForward.manage",
  "request.view",
  "request.viewAll",
  "leave.create",
  "leave.approve",
  "claim.create",
  "claim.approve",
  "timesheet.updateOwn",
] as const;
export type Permission = (typeof permissions)[number];

const employeePermissions: readonly Permission[] = [
  "request.view",
  "leave.create",
  "claim.create",
  "timesheet.updateOwn",
];
const rolePermissions: Record<string, readonly Permission[]> = {
  Employee: employeePermissions,
  HR: [
    ...employeePermissions,
    "user.view",
    "user.create",
    "user.createHr",
    "user.update",
    "organisation.manage",
    "carryForward.manage",
    "request.viewAll",
    "leave.approve",
    "claim.approve",
  ],
  Admin: permissions,
};

export function roleHasPermission(role: string, permission: string): boolean {
  return (
    Object.hasOwn(rolePermissions, role) &&
    rolePermissions[role].some((granted) => granted === permission)
  );
}
