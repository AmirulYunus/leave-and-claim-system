import { addCalendarDays, malaysiaToday } from "./app";

// Level 1 full-time employee created by the freshEmployee fixture.
export const freshJoin = "2024-01-15";

const levelBaseDays = [14, 14, 15, 18];

export function isWeekday(iso: string) {
  const [year, month, day] = iso.split("-").map(Number);
  return ![0, 6].includes(new Date(Date.UTC(year, month - 1, day)).getUTCDay());
}

function weekdayIndex(iso: string) {
  const [year, month, day] = iso.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day)).getUTCDay();
}

function eachDate(start: string, end: string) {
  const dates: string[] = [];
  for (
    let cursor = start;
    cursor <= end && dates.length < 400;
    cursor = addCalendarDays(cursor, 1)
  ) {
    dates.push(cursor);
  }
  return dates;
}

export function completedServiceYears(joined: string, asOf: string) {
  let years = Number(asOf.slice(0, 4)) - Number(joined.slice(0, 4));
  if (asOf.slice(5) < joined.slice(5)) years -= 1;
  return years;
}

// Full-year annual days on one assessment date, or null when the policy is
// still open (29 February, eight or more completed years, or a missing level).
export function annualRateDays(joined: string, asOf: string, level: number) {
  if (joined.slice(5) === "02-29") return null;
  if (level < 1 || level > levelBaseDays.length) return null;
  const years = completedServiceYears(joined, asOf);
  if (years >= 8) return null;
  const reward = years < 2 ? 0 : years < 4 ? 1 : years < 6 ? 2 : 3;
  const floor = years < 2 ? 8 : years < 5 ? 12 : 16;
  return Math.max(levelBaseDays[level - 1] + reward, floor);
}

// LEAVE_POLICY.md earns only months that have ended, counts the joining month
// in full, then rounds that cumulative total up to the next half day. An exact
// half day stays as it is. The screen shows those hours divided by 8.
export function earnedAnnualHours(
  joined: string,
  today: string,
  level = 1,
): number | null {
  if (joined > today) return 0;
  const year = Number(today.slice(0, 4));
  const month = Number(today.slice(5, 7));
  const joinedYear = Number(joined.slice(0, 4));
  const joinedMonth = Number(joined.slice(5, 7));
  const firstMonth = joinedYear === year ? joinedMonth : 1;
  const lastMonth = month - 1;
  let sumOfRates = 0;
  for (let index = firstMonth; index <= lastMonth; index += 1) {
    const endDay = new Date(Date.UTC(year, index, 0)).getUTCDate();
    const monthEnd = `${year}-${String(index).padStart(2, "0")}-${String(endDay).padStart(2, "0")}`;
    const rate = annualRateDays(joined, monthEnd, level);
    if (rate == null) return null;
    sumOfRates += rate;
  }
  const halves = sumOfRates * 2;
  const rounded = halves % 12 === 0 ? halves / 12 : Math.floor(halves / 12) + 1;
  return rounded * 4;
}

export function formatDays(hours: number) {
  return String(Math.round((hours / 8) * 100) / 100);
}

export function leaveSummary(weekdays: number, hoursPerDay: number) {
  const total = weekdays * hoursPerDay;
  const label = weekdays === 1 ? "weekday" : "weekdays";
  return `${weekdays} ${label} × ${hoursPerDay} hours = ${total} hours`;
}

export function leaveAmount(totalHours: number) {
  const days = totalHours / 8;
  return `${totalHours} h (${days} ${days === 1 ? "day" : "days"})`;
}

export type YearPlan = {
  today: string;
  year: string;
  earnedHours: number | null;
  weekdays: string[];
  weekend: { start: string; end: string } | null;
  fridayToMonday: { start: string; end: string } | null;
  nextYearDate: string;
};

// Dates from 1 April onward keep carry-forward hours at 0.
export function yearPlan(
  today = malaysiaToday(),
  joined = freshJoin,
  level = 1,
): YearPlan {
  const year = today.slice(0, 4);
  const dates = eachDate(`${year}-04-01`, `${year}-12-31`);
  const weekdays = dates.filter(isWeekday);
  const upcoming = weekdays.filter((date) => date >= today);
  const earlier = weekdays.filter((date) => date < today).reverse();
  const weekendDate = dates.find((date) => weekdayIndex(date) === 6);
  const friday = dates.find((date) => {
    if (weekdayIndex(date) !== 5) return false;
    return addCalendarDays(date, 3).startsWith(year);
  });
  return {
    today,
    year,
    earnedHours: earnedAnnualHours(joined, today, level),
    weekdays: [...upcoming, ...earlier],
    weekend: weekendDate
      ? { start: weekendDate, end: addCalendarDays(weekendDate, 1) }
      : null,
    fridayToMonday: friday
      ? { start: friday, end: addCalendarDays(friday, 3) }
      : null,
    nextYearDate: `${Number(year) + 1}-01-04`,
  };
}

// weekdays[0] is today when today is a weekday. Callers that need a future date use this.
export function futureWeekday(plan: YearPlan) {
  return plan.weekdays.find((date) => date > plan.today);
}

export function overBalanceSpan(plan: YearPlan) {
  if (plan.earnedHours == null) return null;
  const needed = Math.floor(plan.earnedHours / 8) + 1;
  const dates = eachDate(`${plan.year}-04-01`, `${plan.year}-12-31`).filter(
    isWeekday,
  );
  if (dates.length < needed) return null;
  const chosen = dates.slice(0, needed);
  return {
    start: chosen[0],
    end: chosen[chosen.length - 1],
    weekdays: chosen.length,
    hours: chosen.length * 8,
  };
}

export function weekdaysInMonth(month: string) {
  const [year, mon] = month.split("-").map(Number);
  const last = new Date(Date.UTC(year, mon, 0)).getUTCDate();
  const dates: string[] = [];
  for (let day = 1; day <= last; day += 1) {
    const iso = `${month}-${String(day).padStart(2, "0")}`;
    if (isWeekday(iso)) dates.push(iso);
  }
  return dates;
}

export function shiftMonth(month: string) {
  const [year, mon] = month.split("-").map(Number);
  const next = mon === 12 ? 1 : mon + 1;
  const nextYear = mon === 12 ? year + 1 : year;
  return `${nextYear}-${String(next).padStart(2, "0")}`;
}

// Medical leave is still inside the 48-hour window on this date.
export function medicalWeekday(today: string) {
  const year = today.slice(0, 4);
  for (let offset = 0; offset <= 370; offset += 1) {
    const candidate = addCalendarDays(today, offset);
    if (!candidate.startsWith(year)) break;
    if (isWeekday(candidate)) return candidate;
  }
  const yesterday = addCalendarDays(today, -1);
  if (yesterday.startsWith(year) && isWeekday(yesterday)) return yesterday;
  return null;
}
