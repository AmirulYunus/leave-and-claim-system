export const WORKDAY_HOURS = 8;
export type LeaveEntry = { date: string; hours: number };
export function datesBetween(start: string, end: string): string[] {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(start) || !/^\d{4}-\d{2}-\d{2}$/.test(end))
    return [];
  const from = Date.parse(start + "T00:00:00Z"),
    to = Date.parse(end + "T00:00:00Z");
  if (
    !Number.isFinite(from) ||
    !Number.isFinite(to) ||
    new Date(from).toISOString().slice(0, 10) !== start ||
    new Date(to).toISOString().slice(0, 10) !== end ||
    to < from ||
    to - from > 365 * 86400000
  )
    return [];
  const dates: string[] = [];
  for (let time = from; time <= to; time += 86400000)
    dates.push(new Date(time).toISOString().slice(0, 10));
  return dates;
}
export function isWeekday(date: string) {
  const day = new Date(date + "T00:00:00Z").getUTCDay();
  return day !== 0 && day !== 6;
}
export function serviceYears(joinDate: string, asOf: string) {
  let years = Number(asOf.slice(0, 4)) - Number(joinDate.slice(0, 4));
  if (asOf.slice(5) < joinDate.slice(5)) years--;
  return Math.max(0, years);
}
export function annualAllowance(joinDate: string, asOf: string) {
  return serviceYears(joinDate, asOf) >= 5 ? 16 : 14;
}
export function sickAllowance(joinDate: string, asOf: string) {
  const years = serviceYears(joinDate, asOf);
  return years >= 5 ? 22 : years >= 2 ? 18 : 14;
}
export function validateLeave(
  entries: LeaveEntry[],
  existing: LeaveEntry[],
  availableHours: number,
) {
  if (!entries.length)
    return "Choose at least one eligible date. Date ranges must be within one year.";
  if (
    entries.some(
      (e) => !Number.isFinite(e.hours) || e.hours <= 0 || e.hours > 8,
    )
  )
    return "Leave hours must be greater than zero and no more than 8 per date.";
  if (entries.reduce((sum, e) => sum + e.hours, 0) > availableHours)
    return "This request exceeds your balance after pending requests.";
  if (
    entries.some(
      (e) =>
        e.hours +
          existing
            .filter((other) => other.date === e.date)
            .reduce((sum, other) => sum + other.hours, 0) >
        8,
    )
  )
    return "Leave already requested on this date would exceed 8 hours. Check your existing requests.";
  return "";
}
