import { database } from "./prisma";
import { roleHasPermission, type Permission } from "./permission-policy";
import type { Prisma } from "../generated/prisma/client";

// Read the current database role on every check; never trust a client-supplied role.
// Pass the transaction client when checking permission inside a transaction.
export async function hasPermission(
  userId: number,
  permission: Permission,
  client?: Pick<Prisma.TransactionClient, "employees">,
): Promise<boolean> {
  if (!Number.isSafeInteger(userId) || userId <= 0) return false;
  const user = await (client ?? database()).employees.findUnique({
    where: { id: userId },
    select: { role: true },
  });
  return !!user && roleHasPermission(user.role, permission);
}
