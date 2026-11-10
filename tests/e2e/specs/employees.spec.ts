import { test, expect } from "../support/fixtures";
import { expectError, expectSuccess, signIn } from "../support/app";
import { temporaryPassword, replacementPassword } from "../support/provision";
import { employeeRecord, sqlString } from "../support/records";
import { fillNewEmployee, openAddEmployee, summary } from "../support/ui";
import type { Page } from "@playwright/test";

async function retireExtraAdmins(page: Page) {
  const listed = await page.request.get("/api/employees");
  if (!listed.ok()) throw new Error(await listed.text());
  const body = (await listed.json()) as {
    employees: {
      id: number;
      name: string;
      email: string;
      role: string;
      join_date: string;
      is_active: boolean;
    }[];
  };
  for (const person of body.employees) {
    if (
      person.role !== "Admin" ||
      !person.is_active ||
      person.email === "admin@example.test"
    ) {
      continue;
    }
    const saved = await page.request.patch("/api/employees", {
      data: {
        id: person.id,
        name: person.name,
        join_date: person.join_date,
        role: "Admin",
        is_active: false,
      },
    });
    if (!saved.ok()) {
      throw new Error(
        `Could not deactivate ${person.email}: ${await saved.text()}`,
      );
    }
  }
}

test.describe("Employees", () => {
  test("EMP-POS-01 HR creates an employee with a temporary password", async ({
    hrPage,
    db,
    uniqueTag,
  }) => {
    const email = `e2e.${uniqueTag}@example.test`;
    const name = `E2E ${uniqueTag}`;

    await test.step("Save a new employee", async () => {
      const dialog = await fillNewEmployee(hrPage, {
        name,
        email,
        joinDate: "2026-01-15",
        employmentType: "Full-time",
        level: "1",
        temporaryPassword,
      });
      await expect(
        dialog.getByText(
          "Share the temporary password with the employee. They must change it when they first sign in.",
        ),
      ).toBeVisible();
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(hrPage, "Employee profile saved.");
    });

    await test.step("The row shows an active temporary password", async () => {
      const row = hrPage.getByRole("row").filter({ hasText: email });
      await expect(row).toContainText("Employee");
      await expect(row).toContainText("Full-time · Level 1");
      await expect(row).toContainText("Active · Temporary password");
    });

    await test.step("The profile is stored", async () => {
      expect(await employeeRecord(db, email)).toEqual({
        role: "Employee",
        employmentType: "Full-time",
        level: "1",
        joinDate: "2026-01-15",
        mustChange: "yes",
        active: "yes",
      });
    });
  });

  test("EMP-POS-02 HR creates another HR account", async ({
    hrPage,
    db,
    uniqueTag,
  }) => {
    const email = `e2e.${uniqueTag}@example.test`;
    const name = `E2E ${uniqueTag}`;

    await test.step("The new-profile role list has Employee and HR", async () => {
      const dialog = await openAddEmployee(hrPage);
      const role = dialog.getByLabel("Role");
      await expect(
        role.getByRole("option", { name: "Employee", exact: true }),
      ).toHaveCount(1);
      await expect(
        role.getByRole("option", { name: "HR", exact: true }),
      ).toHaveCount(1);
      await expect(
        role.getByRole("option", { name: "Admin", exact: true }),
      ).toHaveCount(0);
      await dialog.getByLabel("Name").fill(name);
      await dialog.getByLabel("Email").fill(email);
      await dialog.getByLabel("Join date").fill("2026-01-15");
      await role.selectOption("HR");
      await dialog.getByLabel("Employment type").selectOption("Full-time");
      await dialog.getByLabel("Employee level").selectOption("1");
      await dialog.getByLabel("Temporary password").fill(temporaryPassword);
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(hrPage, "Employee profile saved.");
    });

    await test.step("The new account is HR and must change the password", async () => {
      const row = hrPage.getByRole("row").filter({ hasText: email });
      await expect(row).toContainText("HR");
      await expect(row).toContainText("Temporary password");
      expect(await employeeRecord(db, email)).toMatchObject({
        role: "HR",
        mustChange: "yes",
      });
    });
  });

  test("EMP-POS-03 email stays unchanged when the level changes", async ({
    hrPage,
    db,
    uniqueTag,
  }) => {
    const email = `e2e.${uniqueTag}@example.test`;
    const name = `E2E ${uniqueTag}`;

    await test.step("Create the employee", async () => {
      const dialog = await fillNewEmployee(hrPage, {
        name,
        email,
        joinDate: "2026-01-15",
        employmentType: "Full-time",
        level: "1",
        temporaryPassword,
      });
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(hrPage, "Employee profile saved.");
    });

    await test.step("Change the level. Email does not accept typing", async () => {
      await hrPage
        .getByRole("row")
        .filter({ hasText: email })
        .getByRole("button", { name: "Edit" })
        .click();
      const dialog = hrPage.getByRole("dialog");
      await expect(
        dialog.getByRole("heading", { name: "Edit employee" }),
      ).toBeVisible();
      await expect(dialog.getByLabel("Email")).toBeDisabled();
      await expect(
        dialog.getByText(
          "Leave the temporary password blank to keep the current password.",
        ),
      ).toBeVisible();
      await expect(dialog.getByLabel("Role").getByRole("option")).toHaveText([
        "Employee",
      ]);
      await dialog.getByLabel("Employee level").selectOption("2");
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(hrPage, "Employee profile saved.");
      const row = hrPage.getByRole("row").filter({ hasText: email });
      await expect(row).toContainText(email);
      await expect(row).toContainText("Level 2");
    });

    await test.step("The email is unchanged and the level is 2", async () => {
      const stored = await employeeRecord(db, email);
      expect(stored?.level).toBe("2");
      expect(stored?.mustChange).toBe("yes");
      const address = await db.query(
        `SELECT email FROM employees WHERE email = ${sqlString(email)}`,
      );
      expect(address).toEqual([[email]]);
    });
  });

  test("EMP-PERM-01 HR cannot assign or create Admin", async ({
    hrPage,
    db,
    uniqueTag,
  }) => {
    const email = `e2e.emp.perm.${uniqueTag}@example.test`;

    await test.step("The new-profile role list has no Admin", async () => {
      const dialog = await openAddEmployee(hrPage);
      const role = dialog.getByLabel("Role");
      await expect(
        role.getByRole("option", { name: "Employee", exact: true }),
      ).toHaveCount(1);
      await expect(
        role.getByRole("option", { name: "HR", exact: true }),
      ).toHaveCount(1);
      await expect(
        role.getByRole("option", { name: "Admin", exact: true }),
      ).toHaveCount(0);
      await dialog.getByRole("button", { name: "Close employee form" }).click();
    });

    await test.step("The server refuses an Admin create", async () => {
      const response = await hrPage.request.post("/api/employees", {
        data: {
          name: `E2E ${uniqueTag}`,
          email,
          role: "Admin",
          join_date: "2026-01-15",
          temporary_password: temporaryPassword,
          employment_type: "Full-time",
          employee_level: 1,
        },
      });
      expect(response.status()).toBe(403);
      expect(((await response.json()) as { error?: string }).error).toBe(
        "Forbidden",
      );
      expect(await employeeRecord(db, email)).toBeNull();
    });
  });

  test("EMP-POS-04 Admin creates an Admin account", async ({
    adminPage,
    db,
    uniqueTag,
  }) => {
    const email = `e2e.${uniqueTag}@example.test`;

    await test.step("Save a new Admin", async () => {
      const dialog = await fillNewEmployee(adminPage, {
        name: `E2E ${uniqueTag}`,
        email,
        joinDate: "2020-01-15",
        role: "Admin",
        temporaryPassword,
      });
      await expect(
        dialog
          .getByLabel("Role")
          .getByRole("option", { name: "Admin", exact: true }),
      ).toHaveCount(1);
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(adminPage, "Employee profile saved.");
      const row = adminPage.getByRole("row").filter({ hasText: email });
      await expect(row).toContainText("Admin");
      await expect(row).toContainText("Temporary password");
    });

    await test.step("The 2020 join date and Admin role are stored", async () => {
      expect(await employeeRecord(db, email)).toMatchObject({
        role: "Admin",
        joinDate: "2020-01-15",
        mustChange: "yes",
      });
    });
  });

  test("EMP-NEG-01 the last active Admin cannot be deactivated or demoted", async ({
    adminPage,
    accounts,
    db,
  }) => {
    await test.step("Leave exactly one active Admin", async () => {
      await retireExtraAdmins(adminPage);
    });

    await test.step("Deactivation is refused", async () => {
      await adminPage.getByRole("button", { name: "Employees" }).click();
      const row = adminPage
        .getByRole("row")
        .filter({ hasText: accounts.admin.email });
      await row.getByRole("button", { name: "Edit" }).click();
      const dialog = adminPage.getByRole("dialog");
      await dialog.getByRole("checkbox", { name: "Active employee" }).uncheck();
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectError(adminPage, "Keep at least one active Admin account.");
      await dialog.getByRole("button", { name: "Close employee form" }).click();
    });

    await test.step("Demotion is refused", async () => {
      const row = adminPage
        .getByRole("row")
        .filter({ hasText: accounts.admin.email });
      await row.getByRole("button", { name: "Edit" }).click();
      const dialog = adminPage.getByRole("dialog");
      await dialog.getByLabel("Role").selectOption("Employee");
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectError(adminPage, "Keep at least one active Admin account.");
    });

    await test.step("The account stays an active Admin", async () => {
      const row = adminPage
        .getByRole("row")
        .filter({ hasText: accounts.admin.email });
      await expect(row).not.toContainText("Inactive");
      expect(await employeeRecord(db, accounts.admin.email)).toMatchObject({
        role: "Admin",
        active: "yes",
      });
    });
  });

  test("EMP-VAL-01 a duplicate email is refused", async ({ hrPage, db }) => {
    const email = "amirul@example.test";
    const count = async () =>
      (
        await db.query(
          `SELECT count(*)::text FROM employees WHERE email = ${sqlString(email)}`,
        )
      )[0][0];
    expect(await count()).toBe("1");

    await test.step("Save another profile with that email", async () => {
      const dialog = await fillNewEmployee(hrPage, {
        name: "E2E EMP-VAL-01",
        email,
        joinDate: "2026-01-15",
        temporaryPassword,
      });
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectError(hrPage, "An employee already uses that email.");
      await expect(
        dialog.getByRole("heading", { name: "Add employee" }),
      ).toBeVisible();
    });

    await test.step("The original account is the only row", async () => {
      expect(await count()).toBe("1");
    });
  });

  test("EMP-POS-05 blank type and level show Pending, not zero", async ({
    page,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const email = `e2e.${uniqueTag}@example.test`;
    const name = `E2E ${uniqueTag}`;

    await test.step("HR saves an employee without type or level", async () => {
      const dialog = await fillNewEmployee(hrPage, {
        name,
        email,
        joinDate: "2026-01-15",
        temporaryPassword,
      });
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(hrPage, "Employee profile saved.");
      const row = hrPage.getByRole("row").filter({ hasText: email });
      await expect(row).toContainText("Pending HR details");
      await row.getByRole("button", { name: "Edit" }).click();
      await expect(
        hrPage
          .getByRole("dialog")
          .getByText(
            "Leave entitlements stay pending until HR sets the join date, employment type and employee level.",
          ),
      ).toBeVisible();
    });

    await test.step("The employee sees Pending, not zero days", async () => {
      await signIn(page, email, temporaryPassword);
      await page.getByLabel("Temporary password").fill(temporaryPassword);
      await page
        .getByLabel("New password", { exact: true })
        .fill(replacementPassword);
      await page.getByLabel("Confirm new password").fill(replacementPassword);
      await page.getByRole("button", { name: "Save password" }).click();
      await expect(
        page.getByRole("heading", { name: new RegExp(name) }),
      ).toBeVisible();
      await expect(summary(page)).toContainText("Pending HR details or policy");
      await expect(summary(page)).toContainText(
        "Complete active employment details are required to assess earned leave.",
      );
      await expect(summary(page)).not.toContainText("0 days available");
    });

    await test.step("Employment type and level are null", async () => {
      expect(await employeeRecord(db, email)).toMatchObject({
        employmentType: "",
        level: "",
        joinDate: "2026-01-15",
      });
    });
  });
});
