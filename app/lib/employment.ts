// HR-maintained inputs for entitlement rules. A null value means HR has not
// supplied it yet; it is never a default or proof of ineligibility.
export const employmentTypes = ["Full-time", "Part-time", "Intern"] as const;
export const employeeLevels = [
  { value: 1, label: "Operations / Analyst / Senior Analyst" },
  { value: 2, label: "Consultant / Senior Consultant" },
  { value: 3, label: "Sales / Lead Consultant (Manager)" },
  { value: 4, label: "Principal Consultant (Senior Manager) / Director" },
] as const;

export type EmploymentDetails = {
  position: string | null;
  employment_type: string | null;
  employee_level: number | null;
  probation_completed_on: string | null;
  leaving_date: string | null;
  is_active: boolean;
  department_id: number | null;
  cost_centre_id: number | null;
};

export const newEmploymentDetails: EmploymentDetails = {
  position: null,
  employment_type: null,
  employee_level: null,
  probation_completed_on: null,
  leaving_date: null,
  is_active: true,
  department_id: null,
  cost_centre_id: null,
};

const blank = (value: unknown) => value === null || value === "";
const isDate = (value: unknown): value is string =>
  typeof value === "string" &&
  /^\d{4}-\d{2}-\d{2}$/.test(value) &&
  new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
const wholeNumber = (value: unknown) =>
  typeof value === "number"
    ? value
    : typeof value === "string" && /^\d+$/.test(value)
      ? Number(value)
      : NaN;

// Omitted fields keep their current value; null or "" clears an optional field.
// Returns the merged details, or an error message for the first invalid field.
export function parseEmploymentDetails(
  input: Record<string, unknown>,
  joinDate: string,
  current: EmploymentDetails,
): EmploymentDetails | string {
  const next = { ...current };
  if (input.position !== undefined) {
    if (blank(input.position)) next.position = null;
    else if (typeof input.position !== "string" || input.position.length > 100)
      return "Position must be 100 characters or fewer.";
    else next.position = input.position.trim() || null;
  }
  if (input.employment_type !== undefined) {
    if (blank(input.employment_type)) next.employment_type = null;
    else if (!employmentTypes.some((type) => type === input.employment_type))
      return "Choose a valid employment type.";
    else next.employment_type = input.employment_type as string;
  }
  if (input.employee_level !== undefined) {
    const level = wholeNumber(input.employee_level);
    if (blank(input.employee_level)) next.employee_level = null;
    else if (!employeeLevels.some((option) => option.value === level))
      return "Choose a valid employee level.";
    else next.employee_level = level;
  }
  for (const field of ["probation_completed_on", "leaving_date"] as const) {
    if (input[field] === undefined) continue;
    if (blank(input[field])) next[field] = null;
    else if (!isDate(input[field])) return "Enter valid employment dates.";
    else next[field] = input[field];
  }
  if (next.leaving_date && next.leaving_date < joinDate)
    return "Leaving date cannot be before the join date.";
  if (input.is_active !== undefined) {
    if (typeof input.is_active !== "boolean")
      return "Choose whether the employee is active.";
    next.is_active = input.is_active;
  }
  for (const field of ["department_id", "cost_centre_id"] as const) {
    if (input[field] === undefined) continue;
    const id = wholeNumber(input[field]);
    if (blank(input[field])) next[field] = null;
    else if (!Number.isSafeInteger(id) || id <= 0)
      return "Choose a valid department and cost centre.";
    else next[field] = id;
  }
  return next;
}
