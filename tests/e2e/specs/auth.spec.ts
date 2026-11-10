import { test, expect } from "../support/fixtures";
import { expectError, expectSuccess, signIn, signOut } from "../support/app";
import {
  createTemporaryEmployee,
  mismatchedPassword,
  replacementPassword,
  temporaryPassword,
  wrongPassword,
} from "../support/provision";
import { employeeRecord, sessionState } from "../support/records";

test.describe("Sign-in and temporary password", () => {
  test("AUTH-POS-01 seeded employee reaches the dashboard and owns a session", async ({
    page,
    accounts,
    db,
  }) => {
    const email = accounts.employee.email;
    const before = await sessionState(db, email);

    await test.step("Sign in as the seeded employee", async () => {
      await signIn(page, email, accounts.employee.password);
      await expect(
        page.getByRole("heading", { name: /Good morning, Amirul/ }),
      ).toBeVisible();
      await expect(page.getByText("Employee workspace")).toBeVisible();
      for (const name of [
        "Dashboard",
        "My leave",
        "My claims",
        "My timesheet",
      ]) {
        await expect(
          page.getByRole("button", { name, exact: true }),
        ).toBeVisible();
      }
    });

    await test.step("A live session is stored for that employee", async () => {
      const after = await sessionState(db, email);
      expect(after.total).toBe(before.total + 1);
      expect(after.stillValid).toBe(after.total);
      expect((await employeeRecord(db, email))?.mustChange).toBe("no");
    });
  });

  test("AUTH-NEG-01 a wrong password stays on the form and writes no session", async ({
    page,
    accounts,
    db,
  }) => {
    const email = accounts.employee.email;
    const before = await sessionState(db, email);

    await test.step("Sign in with the wrong password", async () => {
      await signIn(page, email, wrongPassword);
      await expectError(page, "Incorrect email or password.");
      await expect(
        page.getByRole("heading", { name: "Sign in to People & Co." }),
      ).toBeVisible();
    });

    await test.step("No session was added", async () => {
      expect(await sessionState(db, email)).toEqual(before);
    });
  });

  test("AUTH-NEG-02 an unknown email gets the same alert and no account", async ({
    page,
    db,
    uniqueTag,
  }) => {
    const email = `nobody.${uniqueTag}@example.test`;

    await test.step("Sign in with an email that is not an employee", async () => {
      await signIn(page, email, wrongPassword);
      await expectError(page, "Incorrect email or password.");
      await expect(
        page.getByRole("heading", { name: "Sign in to People & Co." }),
      ).toBeVisible();
    });

    await test.step("No employee and no session exist for that email", async () => {
      expect(await employeeRecord(db, email)).toBeNull();
      expect(await sessionState(db, email)).toEqual({
        total: 0,
        stillValid: 0,
      });
    });
  });

  test("AUTH-POS-02 sign out returns to the form and deletes that session", async ({
    page,
    accounts,
    db,
  }) => {
    const email = accounts.employee.email;
    const before = await sessionState(db, email);

    await test.step("Sign in", async () => {
      await signIn(page, email, accounts.employee.password);
      await expect(
        page.getByRole("heading", { name: /Good morning, Amirul/ }),
      ).toBeVisible();
    });

    await test.step("Sign out", async () => {
      expect((await sessionState(db, email)).total).toBe(before.total + 1);
      await signOut(page);
    });

    await test.step("That session is gone", async () => {
      expect(await sessionState(db, email)).toEqual(before);
    });
  });

  test("AUTH-POS-03 a new employee must replace the temporary password", async ({
    page,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const created = await createTemporaryEmployee(hrPage, uniqueTag);

    await test.step("Sign in with the temporary password", async () => {
      await signIn(page, created.email, temporaryPassword);
      await expect(
        page.getByRole("heading", { name: "Choose a new password" }),
      ).toBeVisible();
      await expect(page.getByText("Use 12 to 128 characters.")).toBeVisible();
      await expect(
        page.getByRole("heading", { name: /Good morning/ }),
      ).toHaveCount(0);
    });

    await test.step("The account is still waiting for a new password", async () => {
      expect((await employeeRecord(db, created.email))?.mustChange).toBe("yes");
    });

    await test.step("Save a new password", async () => {
      await page.getByLabel("Temporary password").fill(temporaryPassword);
      await page
        .getByLabel("New password", { exact: true })
        .fill(replacementPassword);
      await page.getByLabel("Confirm new password").fill(replacementPassword);
      await page.getByRole("button", { name: "Save password" }).click();
      await expect(
        page.getByRole("heading", {
          name: new RegExp(`Good morning, ${created.name}`),
        }),
      ).toBeVisible();
    });

    await test.step("The temporary-password flag is cleared and a session remains", async () => {
      const record = await employeeRecord(db, created.email);
      expect(record?.mustChange).toBe("no");
      const sessions = await sessionState(db, created.email);
      expect(sessions.total).toBeGreaterThan(0);
      expect(sessions.stillValid).toBe(sessions.total);
    });
  });

  test("AUTH-VAL-01 mismatched new passwords stay on the change screen", async ({
    page,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const created = await createTemporaryEmployee(hrPage, uniqueTag);
    await signIn(page, created.email, temporaryPassword);
    await expect(
      page.getByRole("heading", { name: "Choose a new password" }),
    ).toBeVisible();

    await test.step("Confirm a different new password", async () => {
      await page.getByLabel("Temporary password").fill(temporaryPassword);
      await page
        .getByLabel("New password", { exact: true })
        .fill(replacementPassword);
      await page.getByLabel("Confirm new password").fill(mismatchedPassword);
      await page.getByRole("button", { name: "Save password" }).click();
      await expectError(page, "The new passwords do not match.");
      await expect(
        page.getByRole("heading", { name: "Choose a new password" }),
      ).toBeVisible();
    });

    await test.step("The temporary password is still in force", async () => {
      expect((await employeeRecord(db, created.email))?.mustChange).toBe("yes");
    });
  });

  test("AUTH-VAL-02 the new password must differ from the temporary one", async ({
    page,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const created = await createTemporaryEmployee(hrPage, uniqueTag);
    await signIn(page, created.email, temporaryPassword);

    await test.step("Reuse the temporary password as the new password", async () => {
      await page.getByLabel("Temporary password").fill(temporaryPassword);
      await page
        .getByLabel("New password", { exact: true })
        .fill(temporaryPassword);
      await page.getByLabel("Confirm new password").fill(temporaryPassword);
      await page.getByRole("button", { name: "Save password" }).click();
      await expectError(
        page,
        "Choose a password different from your current one.",
      );
      await expect(
        page.getByRole("heading", { name: "Choose a new password" }),
      ).toBeVisible();
    });

    await test.step("The temporary password is still in force", async () => {
      expect((await employeeRecord(db, created.email))?.mustChange).toBe("yes");
    });
  });

  test("AUTH-NEG-03 a wrong temporary password is rejected", async ({
    page,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const created = await createTemporaryEmployee(hrPage, uniqueTag);
    await signIn(page, created.email, temporaryPassword);

    await test.step("Enter the wrong temporary password", async () => {
      await page.getByLabel("Temporary password").fill(wrongPassword);
      await page
        .getByLabel("New password", { exact: true })
        .fill(replacementPassword);
      await page.getByLabel("Confirm new password").fill(replacementPassword);
      await page.getByRole("button", { name: "Save password" }).click();
      await expectError(page, "Your current password is incorrect.");
    });

    await test.step("The temporary password is still in force", async () => {
      expect((await employeeRecord(db, created.email))?.mustChange).toBe("yes");
    });
  });

  test("AUTH-NEG-04 a deactivated employee gets the same alert as a wrong password", async ({
    page,
    hrPage,
    freshEmployee,
    db,
  }) => {
    await test.step("HR deactivates the employee", async () => {
      await hrPage.getByRole("button", { name: "Employees" }).click();
      const row = hrPage
        .getByRole("row")
        .filter({ hasText: freshEmployee.email });
      await row.getByRole("button", { name: "Edit" }).click();
      const dialog = hrPage.getByRole("dialog");
      await dialog.getByRole("checkbox", { name: "Active employee" }).uncheck();
      await dialog.getByRole("button", { name: "Save employee" }).click();
      await expectSuccess(hrPage, "Employee profile saved.");
      await expect(row).toContainText("Inactive");
    });

    await test.step("The former employee cannot sign in", async () => {
      await signIn(page, freshEmployee.email, freshEmployee.password);
      await expectError(page, "Incorrect email or password.");
      await expect(
        page.getByRole("heading", { name: "Sign in to People & Co." }),
      ).toBeVisible();
    });

    await test.step("The account is inactive and has no session", async () => {
      const record = await employeeRecord(db, freshEmployee.email);
      expect(record?.active).toBe("no");
      expect(await sessionState(db, freshEmployee.email)).toEqual({
        total: 0,
        stillValid: 0,
      });
    });
  });
});
