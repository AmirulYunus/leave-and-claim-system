import type { Prisma } from "../generated/prisma/client";
import { annualEarnedHours } from "./entitlement";
import { balanceCategory } from "./leave-types";

// Annual Leave accounting read from approved requests. Emergency leave and
// employee-requested external training are charged to Annual Leave; each
// approval records how many of its hours came from carry-forward.
type Tx = Prisma.TransactionClient;
const isoDate = (date: Date) => date.toISOString().slice(0, 10);
const yearRange = (year: number) => ({
  gte: new Date(`${year}-01-01T00:00:00Z`),
  lte: new Date(`${year}-12-31T00:00:00Z`),
});

export function employmentOf(employee: {
  join_date: Date;
  employee_level: number | null;
  employment_type: string | null;
  is_active: boolean;
  leaving_date: Date | null;
}) {
  return {
    join_date: isoDate(employee.join_date),
    employee_level: employee.employee_level,
    employment_type: employee.employment_type,
    is_active: employee.is_active,
    leaving_date: employee.leaving_date ? isoDate(employee.leaving_date) : null,
  };
}

// Approved Annual Leave hours in `year`, split into carry-forward and earned.
export async function approvedAnnualUse(
  tx: Tx,
  employeeId: number,
  year: number,
  excludeId?: number,
) {
  const requests = await tx.requests.findMany({
    where: {
      employee_id: employeeId,
      kind: "Leave",
      status: "Approved",
      ...(excludeId ? { id: { not: excludeId } } : {}),
      leave_entries: { some: { date: yearRange(year) } },
    },
    include: { leave_entries: true },
  });
  let hours = 0;
  let carry = 0;
  for (const request of requests) {
    if (
      balanceCategory(request.category, request.training_category) !==
      "Annual leave"
    )
      continue;
    hours += request.leave_entries.reduce(
      (sum, entry) => sum + Number(entry.hours),
      0,
    );
    carry += Number(request.carry_forward_hours);
  }
  return { hours, carry, earned: hours - carry };
}

// The confirmed carry-forward from `sourceYear`, or null before HR confirms.
export function confirmedCarryForward(
  tx: Tx,
  employeeId: number,
  sourceYear: number,
) {
  return tx.carry_forwards.findUnique({
    where: {
      employee_id_source_year: {
        employee_id: employeeId,
        source_year: sourceYear,
      },
    },
  });
}

// Earned Annual Leave left unused in `year` (to date, if it is the current
// year), in whole half days. A held entitlement stays pending and comes back
// as its reason.
export async function unusedAnnualHours(
  tx: Tx,
  employee: Parameters<typeof employmentOf>[0] & { id: number },
  year: number,
  today: string,
): Promise<{ hours: number } | { reason: string }> {
  const earned = annualEarnedHours(employmentOf(employee), year, today);
  if (earned.hours === undefined) return { reason: earned.reason };
  const use = await approvedAnnualUse(tx, employee.id, year);
  return {
    hours: Math.max(0, Math.floor((earned.hours - use.earned) / 4) * 4),
  };
}

// Once a year's carry-forward is confirmed its Annual Leave can no longer
// change.
export async function closedYearProblem(
  tx: Tx,
  employeeId: number,
  year: number,
) {
  return (await confirmedCarryForward(tx, employeeId, year))
    ? `The ${year} carry-forward is confirmed, so ${year} Annual Leave can no longer change.`
    : "";
}
