import { test, expect } from "../support/fixtures";
import { signIn } from "../support/app";
import { employeeRecord, leaveRequest, requestCount } from "../support/records";
import { leaveAmount, leaveSummary, yearPlan } from "../support/rules";
import { openApprovals, requestRow, submitLeave } from "../support/ui";

test.describe("Role permissions", () => {
  test("PERM-POS-01 an employee sees only their own workspace", async ({
    freshEmployee,
  }) => {
    const page = await freshEmployee.signedInPage();

    await test.step("Read the employee navigation", async () => {
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
      await expect(
        page.getByRole("button", { name: /HR approvals/ }),
      ).toHaveCount(0);
      await expect(page.getByRole("button", { name: "Employees" })).toHaveCount(
        0,
      );
      await expect(
        page.getByRole("button", { name: "Apply for leave" }),
      ).toBeVisible();
    });
  });

  test("PERM-POS-02 HR sees approvals and employees", async ({ hrPage }) => {
    await test.step("Read the HR navigation", async () => {
      await expect(hrPage.getByText("HR workspace")).toBeVisible();
      await expect(
        hrPage.getByRole("button", { name: /HR approvals \d+/ }),
      ).toBeVisible();
      await expect(
        hrPage.getByRole("button", { name: "Employees" }),
      ).toBeVisible();
    });

    await test.step("Open Employees", async () => {
      await hrPage.getByRole("button", { name: "Employees" }).click();
      await expect(
        hrPage.getByRole("heading", { name: /Your people, in one place/ }),
      ).toBeVisible();
      await expect(
        hrPage.getByText(
          "HR can add Employee and HR profiles and edit Employee profiles. Admin manages all roles.",
        ),
      ).toBeVisible();
    });
  });

  test("PERM-POS-03 Admin sees management navigation and every role", async ({
    adminPage,
    db,
  }) => {
    const before = await db.query(`SELECT count(*)::text FROM employees`);

    await test.step("Read the Admin profile and open a new employee", async () => {
      await expect(adminPage.getByText("Admin · Superuser")).toBeVisible();
      await expect(
        adminPage.getByRole("button", { name: /HR approvals/ }),
      ).toBeVisible();
      await expect(
        adminPage.getByRole("button", { name: "Employees" }),
      ).toBeVisible();
      await adminPage.getByRole("button", { name: "Employees" }).click();
      await adminPage.getByRole("button", { name: "Add employee" }).click();
      const dialog = adminPage.getByRole("dialog");
      const role = dialog.getByLabel("Role");
      await expect(
        role.getByRole("option", { name: "Employee", exact: true }),
      ).toHaveCount(1);
      await expect(
        role.getByRole("option", { name: "HR", exact: true }),
      ).toHaveCount(1);
      await expect(
        role.getByRole("option", { name: "Admin", exact: true }),
      ).toHaveCount(1);
      await dialog.getByRole("button", { name: "Close employee form" }).click();
    });

    await test.step("Closing the form inserts nobody", async () => {
      expect(await db.query(`SELECT count(*)::text FROM employees`)).toEqual(
        before,
      );
    });
  });

  test("PERM-NEG-01 an employee does not see a colleague's request", async ({
    page,
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = plan.earnedHours ?? 0;
    test.skip(
      plan.earnedHours == null,
      `Annual leave is pending policy on ${plan.today}.`,
    );
    test.skip(
      earned < 8,
      `Only ${earned} annual hours are earned on ${plan.today}.`,
    );
    test.skip(
      plan.weekdays.length === 0,
      `No weekday after 31 March ${plan.year}.`,
    );
    const date = plan.weekdays[0];
    const note = `PERM-NEG-01 ${uniqueTag}`;
    const employee = await freshEmployee.signedInPage();

    await test.step("The employee submits leave", async () => {
      await submitLeave(employee, {
        start: date,
        end: date,
        note,
        summary: leaveSummary(1, 8),
      });
      await expect(requestRow(employee, note)).toContainText("Pending");
      await expect(requestRow(employee, note)).toContainText(leaveAmount(8));
    });

    await test.step("A colleague's leave list does not contain it", async () => {
      await signIn(page, accounts.sarah.email, accounts.sarah.password);
      await page.getByRole("button", { name: "My leave", exact: true }).click();
      await expect(page.getByText(note)).toHaveCount(0);
      await expect(
        page.getByText(
          "No requests here yet. Your next request will appear here.",
        ),
      ).toBeVisible();
    });

    await test.step("HR can see the same request", async () => {
      await openApprovals(hrPage);
      await expect(requestRow(hrPage, note)).toContainText("Pending");
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Pending",
        email: freshEmployee.email,
        kind: "Leave",
      });
      expect(await requestCount(db, note)).toBe(1);
    });
  });

  test("PERM-PERM-01 HR cannot edit an HR or Admin profile", async ({
    hrPage,
    accounts,
    db,
  }) => {
    await test.step("Edit is available only on employee rows", async () => {
      await hrPage.getByRole("button", { name: "Employees" }).click();
      await expect(
        hrPage
          .getByRole("row")
          .filter({ hasText: accounts.hr.email })
          .getByRole("button", { name: "Edit" }),
      ).toBeDisabled();
      await expect(
        hrPage
          .getByRole("row")
          .filter({ hasText: accounts.admin.email })
          .getByRole("button", { name: "Edit" }),
      ).toBeDisabled();
      await expect(
        hrPage
          .getByRole("row")
          .filter({ hasText: accounts.employee.email })
          .getByRole("button", { name: "Edit" }),
      ).toBeEnabled();
    });

    await test.step("The server refuses a change to the Admin profile", async () => {
      const before = await employeeRecord(db, accounts.admin.email);
      const listed = await hrPage.request.get("/api/employees");
      const body = (await listed.json()) as {
        employees: {
          id: number;
          name: string;
          email: string;
          join_date: string;
        }[];
      };
      const admin = body.employees.find(
        (person) => person.email === accounts.admin.email,
      );
      const response = await hrPage.request.patch("/api/employees", {
        data: {
          id: admin?.id,
          name: admin?.name,
          join_date: admin?.join_date,
          role: "Admin",
        },
      });
      expect(response.status()).toBe(403);
      expect(((await response.json()) as { error?: string }).error).toBe(
        "Forbidden",
      );
      expect(await employeeRecord(db, accounts.admin.email)).toEqual(before);
    });
  });

  test("PERM-PERM-02 an employee cannot load employees or decide a request", async ({
    freshEmployee,
    db,
  }) => {
    const page = await freshEmployee.signedInPage();
    const beforeEmployees = await db.query(
      `SELECT count(*)::text FROM employees`,
    );
    const beforeRequest = await db.query(
      `SELECT coalesce(status, '') FROM requests WHERE id = 1`,
    );

    await test.step("The employee calls the employee list and a decision", async () => {
      const listed = await page.request.get("/api/employees");
      expect(listed.status()).toBe(403);
      expect(((await listed.json()) as { error?: string }).error).toBe(
        "Forbidden",
      );
      const decided = await page.request.patch("/api/workspace", {
        data: { id: 1, status: "Approved" },
      });
      expect(decided.status()).toBe(403);
      expect(((await decided.json()) as { error?: string }).error).toBe(
        "Forbidden",
      );
    });

    await test.step("My leave has no decision controls", async () => {
      await page.getByRole("button", { name: "My leave", exact: true }).click();
      await expect(
        page.getByRole("columnheader", { name: "Status" }),
      ).toBeVisible();
      await expect(
        page.getByRole("columnheader", { name: "Decision" }),
      ).toHaveCount(0);
      await expect(
        page.getByRole("button", { name: "Approve", exact: true }),
      ).toHaveCount(0);
      await expect(
        page.getByRole("button", { name: "Reject", exact: true }),
      ).toHaveCount(0);
    });

    await test.step("Nothing was created or decided", async () => {
      expect(await db.query(`SELECT count(*)::text FROM employees`)).toEqual(
        beforeEmployees,
      );
      expect(
        await db.query(
          `SELECT coalesce(status, '') FROM requests WHERE id = 1`,
        ),
      ).toEqual(beforeRequest);
    });
  });

  test("PERM-PERM-03 a signed-out call is refused", async ({ page, db }) => {
    const requests = await db.query(`SELECT count(*)::text FROM requests`);
    const sessions = await db.query(`SELECT count(*)::text FROM sessions`);

    await test.step("Open the sign-in form and call the workspace", async () => {
      await page.goto("/");
      await expect(
        page.getByRole("heading", { name: "Sign in to People & Co." }),
      ).toBeVisible();
      const response = await page.request.get("/api/workspace");
      expect(response.status()).toBe(401);
      expect(((await response.json()) as { error?: string }).error).toBe(
        "Sign in to continue.",
      );
      await expect(
        page.getByRole("heading", { name: "Sign in to People & Co." }),
      ).toBeVisible();
    });

    await test.step("No row was written", async () => {
      expect(await db.query(`SELECT count(*)::text FROM requests`)).toEqual(
        requests,
      );
      expect(await db.query(`SELECT count(*)::text FROM sessions`)).toEqual(
        sessions,
      );
    });
  });
});
