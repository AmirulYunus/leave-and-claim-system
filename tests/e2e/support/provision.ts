import type { Page } from "@playwright/test";
import { freshJoin } from "./rules";

export const temporaryPassword = "e2e-temp-password";
export const replacementPassword = "e2e-new-password-1";
export const mismatchedPassword = "e2e-new-password-2";
export const wrongPassword = "e2e-wrong-password";

export async function createTemporaryEmployee(hrPage: Page, tag: string) {
  const email = `e2e.${tag}@example.test`;
  const name = `E2E ${tag}`;
  const response = await hrPage.request.post("/api/employees", {
    data: {
      name,
      email,
      role: "Employee",
      join_date: freshJoin,
      temporary_password: temporaryPassword,
      employment_type: "Full-time",
      employee_level: 1,
      probation_completed_on: "2024-04-15",
      is_active: true,
    },
  });
  if (!response.ok()) {
    throw new Error(
      `Creating ${email} failed: ${response.status()} ${await response.text()}`,
    );
  }
  return { email, name, temporaryPassword };
}
