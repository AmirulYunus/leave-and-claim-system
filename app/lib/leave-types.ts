import { datesBetween, isWeekday, type LeaveEntry } from "./time";

// Leave categories and their date, duration, detail and approval rules, ported
// from the Dataverse plug-ins (LEAVE_RULES_RECHECK_2026-10-25.md). HR is the
// approver in this application. Each function returns "" when the rule passes
// or the message to show.

export const leaveCategories = [
  "Annual leave",
  "Medical leave",
  "Hospitalisation leave",
  "Emergency leave",
  "Training leave",
  "Maternity leave",
  "Paternity leave",
  "Unpaid leave",
] as const;
export const trainingCategories = [
  "Company Required",
  "Employee-Requested External",
] as const;

// Consecutive calendar-day categories and their fixed length in days, taken
// in full days. Every other category covers each weekday in its range with one
// half-day (4 hours) or full-day (8 hours) duration.
export const fixedLengthDays: Record<string, number> = {
  "Maternity leave": 98,
  "Paternity leave": 7,
};

export type LeaveDetails = {
  expected_birth_on: string | null;
  birth_on: string | null;
  notified_on: string | null;
  training_category: string | null;
};

export const isFixedLength = (category: string) => category in fixedLengthDays;

// Which balance a request uses. Emergency Leave and employee-requested
// external training are charged to Annual Leave.
export function balanceCategory(
  category: string,
  trainingCategory: string | null,
) {
  if (category === "Emergency leave") return "Annual leave";
  if (category === "Training leave")
    return trainingCategory === "Employee-Requested External"
      ? "Annual leave"
      : null;
  if (
    ["Annual leave", "Medical leave", "Hospitalisation leave"].includes(
      category,
    )
  )
    return category;
  return null;
}

export function addDays(date: string, days: number) {
  return new Date(Date.parse(`${date}T00:00:00Z`) + days * 86400_000)
    .toISOString()
    .slice(0, 10);
}

// The same calendar date `months` earlier, clamped to the month's last day.
export function subtractMonths(date: string, months: number) {
  const [year, month, day] = date.split("-").map(Number);
  const target = new Date(Date.UTC(year, month - 1 - months, 1));
  const last = new Date(
    Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0),
  ).getUTCDate();
  target.setUTCDate(Math.min(day, last));
  return target.toISOString().slice(0, 10);
}

// The dates a request covers, generated from its range.
export function scheduleDates(category: string, start: string, end: string) {
  const length = fixedLengthDays[category];
  if (length) return datesBetween(start, addDays(start, length - 1));
  return datesBetween(start, end).filter(isWeekday);
}

// Category-specific date and duration rules for the saved entries.
export function scheduleProblem(category: string, entries: LeaveEntry[]) {
  const dates = entries.map((entry) => entry.date).sort();
  const length = fixedLengthDays[category];
  if (length) {
    const expected = datesBetween(dates[0], addDays(dates[0], length - 1));
    if (
      dates.length !== length ||
      expected.some((date, index) => date !== dates[index]) ||
      entries.some((entry) => entry.hours !== 8)
    )
      return `${category} covers ${length} consecutive calendar days in full days.`;
    return "";
  }
  const weekdays = datesBetween(dates[0], dates.at(-1) ?? "").filter(isWeekday);
  if (
    weekdays.length !== dates.length ||
    weekdays.some((date, index) => date !== dates[index])
  )
    return `${category} must include every weekday in its date range.`;
  if (
    ![4, 8].includes(entries[0].hours) ||
    entries.some((entry) => entry.hours !== entries[0].hours)
  )
    return `${category} uses one duration for every date: half day (4 hours) or full day (8 hours).`;
  return "";
}

// Details required when the employee submits.
export function submissionProblem(category: string, details: LeaveDetails) {
  if (
    category === "Maternity leave" &&
    (!details.expected_birth_on || !details.notified_on)
  )
    return "Maternity leave needs the expected confinement date and the date you gave formal notice.";
  if (
    category === "Paternity leave" &&
    (!details.notified_on || (!details.expected_birth_on && !details.birth_on))
  )
    return "Paternity leave needs a notice date and an expected or actual birth date.";
  if (
    category === "Training leave" &&
    !trainingCategories.some((option) => option === details.training_category)
  )
    return "Choose whether the training is company-required or employee-requested external training.";
  return "";
}

// Rules HR's approval must satisfy beyond the submission rules. The
// maternity 60-day notice window is left to HR's judgement, not enforced.
export function approvalProblem(
  category: string,
  input: {
    details: LeaveDetails;
    start: string;
    joinDate: string;
    attachments: number;
    priorBirths: (string | null)[];
  },
) {
  const submitted = submissionProblem(category, input.details);
  if (submitted) return submitted;
  if (category === "Emergency leave" && input.attachments < 1)
    return "Emergency leave needs supporting evidence before approval. HR must verify it.";
  if (category !== "Paternity leave") return "";
  const birth = input.details.birth_on;
  if (!birth || birth !== input.start)
    return "Paternity leave must start on the verified birth date.";
  if (input.joinDate > subtractMonths(input.start, 12))
    return "Paternity leave requires at least 12 months of continuous service before it starts; check the join date.";
  // A prior approval without a recorded birth date counts as a separate birth.
  const known = new Set(input.priorBirths.filter(Boolean));
  const unrecorded = input.priorBirths.filter((date) => !date).length;
  if (known.has(birth))
    return "Paternity leave has already been approved for this birth.";
  if (known.size + unrecorded >= 5)
    return "Five earlier paternity confinements are already recorded. Review the history.";
  return "";
}
