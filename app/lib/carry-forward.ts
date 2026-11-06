// Annual Leave carry-forward, ported from the Dataverse CarryForwardDraft,
// CarryForwardAllocation and CarryForwardExceptionPolicy rules. Amounts are
// hours in half-day (4-hour) steps. Unused leave above the limit is recorded,
// never expired or forfeited by this code.
import type { LeaveEntry } from "./time";

export const standardCarryLimitHours = 40;
export const carryUseBy = (year: number) => `${year}-03-31`;

export function carryForwardDraft(
  unusedHours: number,
  exceptionLimit?: number,
) {
  const limit = exceptionLimit ?? standardCarryLimitHours;
  const carry = Math.min(Math.max(0, unusedHours), limit);
  return { unusedHours, limit, carry, remainder: unusedHours - carry };
}

// HR records a written higher limit from 1 October to 15 December of the
// closing year.
export function exceptionProblem(input: {
  sourceYear: number;
  today: string;
  limitHours: number;
  documentReference: string;
}) {
  const { sourceYear, today } = input;
  if (today < `${sourceYear}-10-01` || today > `${sourceYear}-12-15`)
    return "Written carry-forward exceptions must be recorded from 1 October to 15 December of the closing year.";
  if (
    !Number.isInteger(input.limitHours) ||
    input.limitHours <= standardCarryLimitHours ||
    input.limitHours % 4 !== 0 ||
    input.limitHours > 1000
  )
    return "An exception limit must exceed five days, in half-day steps, up to 1,000 hours.";
  const reference = input.documentReference.trim();
  if (!reference || reference.length > 200)
    return "Enter the written approval's document reference (up to 200 characters).";
  return "";
}

// HR confirms from 1 January to 31 March after the closing year. HR may carry
// less than calculated, with a reason and document reference, never more.
export function confirmationProblem(input: {
  sourceYear: number;
  today: string;
  carryHours: number;
  draft: { carry: number };
  reason: string;
  documentReference: string;
}) {
  const target = input.sourceYear + 1;
  if (input.today < `${target}-01-01` || input.today > carryUseBy(target))
    return `Carry-forward from ${input.sourceYear} can be confirmed from 1 January to 31 March ${target}.`;
  if (
    !Number.isInteger(input.carryHours) ||
    input.carryHours < 0 ||
    input.carryHours % 4 !== 0 ||
    input.carryHours > input.draft.carry
  )
    return `Carry forward at most ${input.draft.carry} hours, in half-day steps.`;
  if (
    input.carryHours !== input.draft.carry &&
    (!input.reason.trim() || !input.documentReference.trim())
  )
    return "Reducing the carry-forward needs a reason and a document reference.";
  if (input.reason.length > 2000 || input.documentReference.length > 200)
    return "Keep the reason under 2,000 characters and the reference under 200.";
  return "";
}

// Hours of a request funded by carry-forward: spent first, date by date, on
// dates up to the use-by date.
export function carryAllocation(
  entries: LeaveEntry[],
  availableCarry: number,
  useBy: string,
) {
  let remaining = Math.max(0, availableCarry);
  let carried = 0;
  for (const entry of [...entries].sort((a, b) =>
    a.date.localeCompare(b.date),
  )) {
    if (entry.date > useBy || remaining <= 0) continue;
    const used = Math.min(entry.hours, remaining);
    carried += used;
    remaining -= used;
  }
  return carried;
}
