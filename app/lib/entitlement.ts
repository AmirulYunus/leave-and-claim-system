// Leave entitlement rates and annual accrual, ported from the Dataverse
// EntitlementCalculator and CalendarAnnualGrant rules. Dates are YYYY-MM-DD
// calendar dates in Malaysia time. Missing or unsupported inputs never become a
// zero entitlement; they produce a pending status with the reason.

export type RateStatus =
  "Calculated" | "PendingHrDetails" | "InvalidDetails" | "PendingPolicy";
export type Rate = {
  status: RateStatus;
  reason: string;
  days: number | null;
  hours: number | null;
};
export type EntitlementInputs = {
  join_date: string | null;
  employee_level: number | null;
  employment_type: string | null;
};

const supportedTypes = ["Full-time", "Part-time", "Intern"];
const levelBaseDays = [14, 14, 15, 18];

export function malaysiaDate(now = new Date()) {
  return new Date(now.getTime() + 8 * 3600_000).toISOString().slice(0, 10);
}

const parts = (date: string) => date.split("-").map(Number);
const daysInMonth = (year: number, month: number) =>
  new Date(Date.UTC(year, month, 0)).getUTCDate();
const pad = (value: number) => String(value).padStart(2, "0");

export function completedServiceYears(joined: string, asOf: string) {
  let years = parts(asOf)[0] - parts(joined)[0];
  if (asOf.slice(5) < joined.slice(5)) years--;
  return years;
}

const pending = (status: RateStatus, reason: string): Rate => ({
  status,
  reason,
  days: null,
  hours: null,
});
const calculated = (days: number): Rate => ({
  status: "Calculated",
  reason: "Full-year rate",
  days,
  hours: days * 8,
});

// Full-year rates on the assessment date. These are not balances.
export function entitlementRates(inputs: EntitlementInputs, asOf: string) {
  const { join_date: joined, employee_level: level } = inputs;
  const type = inputs.employment_type;
  const missing = [
    ...(joined ? [] : ["Date Joined"]),
    ...(type ? [] : ["Employment Type"]),
  ];
  const annualMissing = [...missing, ...(level ? [] : ["Employee Level"])];
  const result = {
    completedYears: null as number | null,
    annual: pending(
      "PendingHrDetails",
      `Pending HR details: ${annualMissing.join(", ")}`,
    ),
    sick: pending(
      "PendingHrDetails",
      `Pending HR details: ${missing.join(", ")}`,
    ),
    hospitalisation: pending(
      "PendingHrDetails",
      `Pending HR details: ${missing.join(", ")}`,
    ),
  };
  const all = (rate: Rate) => {
    result.annual = result.sick = result.hospitalisation = rate;
    return result;
  };
  if (joined && joined > asOf)
    return all(
      pending(
        "InvalidDetails",
        "HR must correct Date Joined; it must be a calendar date on or before the assessment date.",
      ),
    );
  if (type && !supportedTypes.includes(type))
    return all(
      pending("InvalidDetails", "HR must select a supported Employment Type."),
    );
  if (missing.length || !joined) return result;
  if (type !== "Full-time") {
    result.sick = result.hospitalisation = pending(
      "PendingPolicy",
      "Part-time/intern paid-leave rules require policy and statutory review; no zero entitlement is assigned.",
    );
    if (!annualMissing.length)
      result.annual = pending(
        "PendingPolicy",
        "Part-time/intern annual-leave rules require policy and statutory review.",
      );
    return result;
  }
  if (joined.slice(5) === "02-29")
    return all(
      pending(
        "PendingPolicy",
        "29 February service-anniversary handling requires confirmation.",
      ),
    );
  const years = completedServiceYears(joined, asOf);
  result.completedYears = years;
  result.sick = calculated(years < 2 ? 14 : years < 5 ? 18 : 22);
  result.hospitalisation = calculated(60);
  if (annualMissing.length || !level) return result;
  if (level < 1 || level > levelBaseDays.length) {
    result.annual = pending(
      "InvalidDetails",
      "HR must select a supported Employee Level.",
    );
    return result;
  }
  if (years >= 8) {
    result.annual = pending(
      "PendingPolicy",
      "Annual service reward from eight completed years onward is on hold.",
    );
    return result;
  }
  const reward = years < 2 ? 0 : years < 4 ? 1 : years < 6 ? 2 : 3;
  const floor = years < 2 ? 8 : years < 5 ? 12 : 16;
  result.annual = calculated(
    Math.max(levelBaseDays[level - 1] + reward, floor),
  );
  return result;
}

// Sum each completed calendar month's annual rate in asOf's year, then round
// the total up once to the next half-day (4 hours). The joining month counts in
// full once it has ended; the current month is never earned early.
export function cumulativeEarnedHours(
  joined: string,
  asOf: string,
  annualDaysAt: (monthEnd: string) => number,
  yearClosed = false,
) {
  if (asOf < joined) return 0;
  const [year, month] = parts(asOf);
  const [joinedYear, joinedMonth] = parts(joined);
  const firstMonth = joinedYear === year ? joinedMonth : 1;
  const lastMonth = yearClosed ? 12 : month - 1;
  let sumOfRates = 0;
  for (let m = firstMonth; m <= lastMonth; m++) {
    const days = annualDaysAt(`${year}-${pad(m)}-${daysInMonth(year, m)}`);
    if (days <= 0)
      throw new RangeError("Each month's annual rate must be positive.");
    sumOfRates += days;
  }
  return Math.floor((sumOfRates * 2 + 11) / 12) * 4;
}

export type EarnedAnnual =
  { hours: number; reason?: undefined } | { hours?: undefined; reason: string };
type Employment = EntitlementInputs & {
  is_active: boolean | null;
  leaving_date: string | null;
};

// Hours of a leave category the employee may use in `year`, or why none can be
// used yet. Medical and Hospitalisation are full-year rates; Annual is earned
// monthly. Categories without an entitlement (Unpaid) are unlimited.
export function leaveAllowanceHours(
  employee: Employment,
  category: string,
  year: number,
  today: string,
): EarnedAnnual {
  if (category === "Annual leave")
    return annualEarnedHours(employee, year, today);
  const kind =
    category === "Medical leave"
      ? "sick"
      : category === "Hospitalisation leave"
        ? "hospitalisation"
        : null;
  if (!kind) return { hours: Infinity };
  if (!employee.is_active)
    return { reason: "Leave is unavailable for an inactive employee." };
  const rate = entitlementRates(employee, today)[kind];
  return rate.hours === null ? { reason: rate.reason } : { hours: rate.hours };
}

// Annual leave earned in `year` as of `today`, the most that may be approved.
// A future year has earned nothing; a closed year has earned all 12 months.
export function annualEarnedHours(
  employee: Employment,
  year: number,
  today: string,
): EarnedAnnual {
  const joined = employee.join_date;
  if (
    !employee.is_active ||
    !joined ||
    !employee.employee_level ||
    !employee.employment_type
  )
    return {
      reason:
        "Complete active employment details are required to assess earned leave.",
    };
  const leaving = employee.leaving_date;
  if (leaving) {
    const leavingYear = parts(leaving)[0];
    if (leaving < joined)
      return { reason: "Leaving date precedes joining date." };
    if (leavingYear === year)
      return { reason: "Last-year annual leave policy remains pending." };
    if (leavingYear < year)
      return { reason: "The employee left before this balance year." };
  }
  const currentYear = parts(today)[0];
  if (year > currentYear) return { hours: 0 };
  const through = year === currentYear ? today : `${year}-12-31`;
  if (joined > through)
    return {
      reason: "The employee had not joined by the balance assessment date.",
    };
  let unresolved = "";
  const hours = cumulativeEarnedHours(
    joined,
    through,
    (monthEnd) => {
      const rate = entitlementRates(employee, monthEnd).annual;
      if (rate.days === null) {
        unresolved ||= rate.reason;
        return 1;
      }
      return rate.days;
    },
    year < currentYear,
  );
  return unresolved
    ? { reason: `Annual leave rate is unresolved: ${unresolved}` }
    : { hours };
}
