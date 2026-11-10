import { test, expect } from "../support/fixtures";
import { expectError, signIn } from "../support/app";

test("HR signs in and sees the HR view", async ({ hrPage }) => {
  await expect(
    hrPage.getByRole("heading", { name: /Good morning, HR/ }),
  ).toBeVisible();
  await expect(
    hrPage.getByRole("button", { name: /HR approvals/ }),
  ).toBeVisible();
  await expect(hrPage.getByRole("button", { name: "Employees" })).toBeVisible();
});

test("wrong password shows the error and does not sign in", async ({
  page,
  accounts,
  db,
}) => {
  const email = accounts.hr.email;
  const before = await db.query(
    `SELECT count(*) FROM sessions s JOIN employees e ON e.id = s.employee_id WHERE e.email = '${email}'`,
  );
  await signIn(page, email, "not-the-real-password");
  await expectError(page, "Incorrect email or password.");
  await expect(
    page.getByRole("heading", { name: "Sign in to People & Co." }),
  ).toBeVisible();
  expect(
    await db.query(
      `SELECT count(*) FROM sessions s JOIN employees e ON e.id = s.employee_id WHERE e.email = '${email}'`,
    ),
  ).toEqual(before);
});
