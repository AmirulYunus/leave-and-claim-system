import { expect, type Page } from "@playwright/test";
import { formatDays } from "./rules";

export function summary(page: Page) {
  return page.getByRole("region", { name: "Your summary" });
}

export async function expectAnnualDays(page: Page, availableHours: number) {
  await expect(summary(page)).toContainText(
    `${formatDays(availableHours)} days available`,
  );
}

export async function expectAnnualFooter(
  page: Page,
  usedHours: number,
  earnedHours: number,
) {
  await expect(summary(page)).toContainText(
    `${formatDays(usedHours)} of ${formatDays(earnedHours)} earned days used`,
  );
}

export function requestRow(page: Page, note: string) {
  return page.getByRole("row").filter({ hasText: note });
}

export async function submitLeave(
  page: Page,
  input: {
    start: string;
    end: string;
    note: string;
    category?: string;
    hours?: 4 | 8;
    summary?: string;
    help?: string | RegExp;
  },
) {
  await page
    .getByRole("button", { name: "Apply for leave", exact: true })
    .click();
  const dialog = page.getByRole("dialog");
  await expect(
    dialog.getByRole("heading", { name: "Make time for yourself." }),
  ).toBeVisible();
  if (input.category) {
    await dialog.getByLabel("Category").selectOption(input.category);
  }
  const start = dialog.getByLabel("Start date");
  await start.fill(input.start);
  await expect(start).toHaveValue(input.start);
  const end = dialog.getByLabel("End date");
  await end.fill(input.end);
  await expect(end).toHaveValue(input.end);
  if (input.hours === 4) {
    await dialog
      .getByLabel("Duration for every date")
      .selectOption({ label: "Half day (4 hours)" });
  }
  await dialog.getByLabel("Reason").fill(input.note);
  if (input.help) await expect(dialog.getByText(input.help)).toBeVisible();
  if (input.summary)
    await expect(dialog.getByText(input.summary)).toBeVisible();
  await dialog.getByRole("button", { name: "Submit for approval" }).click();
  return dialog;
}

export async function submitClaim(
  page: Page,
  input: {
    category: string;
    description: string;
    amount: string;
    note: string;
  },
) {
  await page.getByRole("button", { name: /Submit a claim/ }).click();
  const dialog = page.getByRole("dialog");
  await expect(
    dialog.getByRole("heading", { name: "Submit an expense claim." }),
  ).toBeVisible();
  await dialog.getByLabel("Category").selectOption(input.category);
  await dialog.getByLabel("Description").fill(input.description);
  await dialog.getByLabel("Amount (RM)").fill(input.amount);
  await dialog.getByLabel("Additional details").fill(input.note);
  await dialog.getByRole("button", { name: "Submit for approval" }).click();
  return dialog;
}

async function reloadWorkspace(page: Page) {
  await page.reload();
  await expect(
    page.getByRole("button", { name: "Apply for leave", exact: true }),
  ).toBeEnabled();
}

export async function openApprovals(page: Page) {
  await reloadWorkspace(page);
  await page.getByRole("button", { name: /HR approvals/ }).click();
  await expect(
    page.getByRole("heading", { name: /A little review goes a long way/ }),
  ).toBeVisible();
}

export async function decide(
  page: Page,
  note: string,
  decision: "Approve" | "Reject",
) {
  await openApprovals(page);
  await page.getByRole("button", { name: "Pending", exact: true }).click();
  const row = requestRow(page, note);
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: decision, exact: true }).click();
  return row;
}

export async function showDecision(
  page: Page,
  note: string,
  status: "Approved" | "Rejected" | "Pending",
) {
  await page.getByRole("button", { name: status, exact: true }).click();
  const row = requestRow(page, note);
  await expect(row).toContainText(status);
  if (status !== "Pending") await expect(row).toContainText("Reviewed");
  return row;
}

export async function openTimesheet(page: Page, month?: string) {
  await reloadWorkspace(page);
  await page.getByRole("button", { name: "My timesheet", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Monthly timesheet" }),
  ).toBeVisible();
  if (!month) return;
  const picker = page.getByLabel("Timesheet month");
  if ((await picker.inputValue()) !== month) await picker.fill(month);
  await expect(picker).toHaveValue(month);
}

export function hoursField(page: Page, date: string) {
  return page.getByLabel(`Worked hours ${date}`);
}

export function timesheetRow(page: Page, date: string) {
  return page.getByRole("row").filter({
    has: page.getByLabel(`Worked hours ${date}`),
  });
}

export async function openAddEmployee(page: Page) {
  await page.getByRole("button", { name: "Employees", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: /Your people, in one place/ }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Add employee" }).click();
  const dialog = page.getByRole("dialog");
  await expect(
    dialog.getByRole("heading", { name: "Add employee" }),
  ).toBeVisible();
  return dialog;
}

export async function fillNewEmployee(
  page: Page,
  input: {
    name: string;
    email: string;
    joinDate: string;
    role?: string;
    employmentType?: string;
    level?: string;
    temporaryPassword: string;
  },
) {
  const dialog = await openAddEmployee(page);
  await dialog.getByLabel("Name").fill(input.name);
  await dialog.getByLabel("Email").fill(input.email);
  await dialog.getByLabel("Join date").fill(input.joinDate);
  if (input.role) await dialog.getByLabel("Role").selectOption(input.role);
  if (input.employmentType) {
    await dialog
      .getByLabel("Employment type")
      .selectOption(input.employmentType);
  }
  if (input.level) {
    await dialog.getByLabel("Employee level").selectOption(input.level);
  }
  await dialog.getByLabel("Temporary password").fill(input.temporaryPassword);
  return dialog;
}
