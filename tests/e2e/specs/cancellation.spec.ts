import { type Page } from "@playwright/test";
import { test, expect, type Database } from "../support/fixtures";
import { answerDialogs, expectError, expectSuccess } from "../support/app";
import {
  employeeId,
  employmentInputs,
  expectDecisionMail,
  leaveRequest,
  notificationSubjects,
  sqlString,
  takenLeaveDates,
  usedAnnualHours,
} from "../support/records";
import {
  earnedAnnualHours,
  freshJoin,
  type YearPlan,
  yearPlan,
} from "../support/rules";
import {
  decide,
  expectAnnualDays,
  expectAnnualFooter,
  openApprovals,
  requestRow,
  submitLeave,
} from "../support/ui";

const blankReasonError = "Give a reason of up to 500 characters.";
const startedError =
  "Cancelling leave that has already started or been taken is on hold; every date must be in the future.";
const duplicateError = "A cancellation is already pending for this request.";
const colleagueError =
  "Only the employee can request cancellation of future leave. HR can correct Emergency leave that has started.";
const ownDecisionError =
  "You cannot decide the cancellation of your own leave. Another HR member or Admin must review it.";

function requireEarned(plan: YearPlan) {
  test.skip(
    plan.earnedHours == null,
    `Annual leave is pending policy on ${plan.today} for a level 1 employee who joined ${freshJoin}.`,
  );
  test.skip(
    (plan.earnedHours ?? 0) < 8,
    `Only ${plan.earnedHours} annual hours are earned on ${plan.today}; this case needs 8.`,
  );
  return plan.earnedHours ?? 0;
}

function futureDate(plan: YearPlan) {
  const date = plan.weekdays.find((item) => item > plan.today);
  test.skip(
    date == null,
    `No weekday after ${plan.today} remains in ${plan.year}.`,
  );
  return date ?? plan.today;
}

function startedDate(plan: YearPlan) {
  const date = plan.weekdays.find((item) => item <= plan.today);
  test.skip(
    date == null,
    `No weekday on or before ${plan.today} remains in ${plan.year}.`,
  );
  return date ?? plan.today;
}

async function openMyLeave(page: Page) {
  await page.reload();
  await expect(
    page.getByRole("button", { name: "Apply for leave", exact: true }),
  ).toBeEnabled();
  await page.getByRole("button", { name: "My leave", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: /Your time, thoughtfully planned/ }),
  ).toBeVisible();
}

async function approveLeave(
  page: Page,
  hrPage: Page,
  note: string,
  date: string,
) {
  await test.step("The employee submits one weekday", async () => {
    await submitLeave(page, { start: date, end: date, note });
    await expectSuccess(page, "Leave request saved for HR review.");
  });

  await test.step("HR approves it", async () => {
    await decide(hrPage, note, "Approve");
    await expectSuccess(hrPage, "Request approved and saved.");
  });
}

async function askToCancel(page: Page, note: string, reason: string | null) {
  await openMyLeave(page);
  await answerDialogs(page, [reason]);
  await requestRow(page, note)
    .getByRole("button", { name: "Request cancellation" })
    .click();
}

async function cancellationRows(db: Database, note: string) {
  return db.query(
    `SELECT c.kind, c.reason, c.status, coalesce(rv.email, ''), r.status
     FROM cancellations c
     JOIN requests r ON r.id = c.request_id
     LEFT JOIN employees rv ON rv.id = c.reviewed_by
     WHERE r.note = ${sqlString(note)}
     ORDER BY c.id`,
  );
}

async function requestId(db: Database, note: string) {
  const rows = await db.query(
    `SELECT id::text FROM requests WHERE note = ${sqlString(note)}`,
  );
  return Number(rows[0][0]);
}

async function bodiesOmit(db: Database, name: string, reason: string) {
  const rows = await db.query(
    `SELECT body FROM notifications WHERE body LIKE ${sqlString(`%${name}%`)}`,
  );
  for (const [body] of rows) expect(body).not.toContain(reason);
}

test.describe("Leave cancellation", () => {
  test("LEAVE-POS-06 a future approved day stays approved while cancellation is pending", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-POS-06 ${uniqueTag}`;
    const reason = `Plans changed ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("The card shows the earned balance", async () => {
      await expectAnnualDays(page, earned);
    });

    await approveLeave(page, hrPage, note, date);

    await test.step("The card drops by one day", async () => {
      await openMyLeave(page);
      await expectAnnualDays(page, earned - 8);
      await expectAnnualFooter(page, 8, earned);
    });

    await test.step("The employee asks HR to cancel that day", async () => {
      await askToCancel(page, note, reason);
      await expectSuccess(page, "Cancellation sent to HR for review.");
      const row = requestRow(page, note);
      await expect(row).toContainText("Approved");
      await expect(row).toContainText(`Cancellation requested: ${reason}`);
      await expect(
        row.getByRole("button", { name: "Request cancellation" }),
      ).toHaveCount(0);
      await expect(
        row.getByRole("button", { name: "Cancel leave" }),
      ).toHaveCount(0);
      await expectAnnualDays(page, earned - 8);
    });

    await test.step("The leave stays approved and no decision email is queued", async () => {
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
        category: "Annual leave",
        email: freshEmployee.email,
      });
      expect(await usedAnnualHours(db, freshEmployee.email, plan.year)).toBe(8);
      expect(await cancellationRows(db, note)).toEqual([
        ["Cancellation", reason, "Pending", "", "Approved"],
      ]);
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your leave request was approved",
        hrSubject: "Leave request approved",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
      const subjects = (
        await notificationSubjects(db, freshEmployee.email, freshEmployee.name)
      ).map((row) => row[1]);
      expect(subjects).not.toContain("Your leave cancellation was approved");
      expect(subjects).not.toContain("Leave cancellation approved");
    });
  });

  test("LEAVE-POS-07 HR cancellation returns the day and queues the decision emails", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-POS-07 ${uniqueTag}`;
    const reason = `Plans changed ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();
    const reviewer = await employeeId(db, accounts.hr.email);

    await approveLeave(page, hrPage, note, date);

    await test.step("The employee asks HR to cancel that day", async () => {
      await askToCancel(page, note, reason);
      await expectSuccess(page, "Cancellation sent to HR for review.");
    });

    await test.step("HR cancels the leave", async () => {
      await openApprovals(hrPage);
      const row = requestRow(hrPage, note);
      await expect(
        row.getByRole("button", { name: "Cancel leave" }),
      ).toBeVisible();
      await expect(
        row.getByRole("button", { name: "Keep leave" }),
      ).toBeVisible();
      await row.getByRole("button", { name: "Cancel leave" }).click();
      await expectSuccess(hrPage, "Leave cancelled and its hours returned.");
      await expect(row).toContainText("Cancelled");
      await expect(row).toContainText("Reviewed");
    });

    await test.step("The day is back on the card", async () => {
      await openMyLeave(page);
      await expectAnnualDays(page, earned);
      await expectAnnualFooter(page, 0, earned);
      await expect(requestRow(page, note)).toContainText("Cancelled");
    });

    await test.step("The cancellation is approved and the emails omit the reason", async () => {
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Cancelled",
        reviewerId: reviewer,
        email: freshEmployee.email,
      });
      expect(await usedAnnualHours(db, freshEmployee.email, plan.year)).toBe(0);
      expect(await cancellationRows(db, note)).toEqual([
        ["Cancellation", reason, "Approved", accounts.hr.email, "Cancelled"],
      ]);
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your leave cancellation was approved",
        hrSubject: "Leave cancellation approved",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
      await bodiesOmit(db, freshEmployee.name, reason);
    });
  });

  test("LEAVE-POS-08 a rejected cancellation keeps the leave and the used day", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-POS-08 ${uniqueTag}`;
    const reason = `Need the day ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("The employee asks HR to cancel that day", async () => {
      await askToCancel(page, note, reason);
      await expectSuccess(page, "Cancellation sent to HR for review.");
    });

    await test.step("HR keeps the leave", async () => {
      await openApprovals(hrPage);
      const row = requestRow(hrPage, note);
      await row.getByRole("button", { name: "Keep leave" }).click();
      await expectSuccess(
        hrPage,
        "Cancellation rejected; the leave stays approved.",
      );
      await expect(row).toContainText("Approved");
      await expect(row).toContainText("Reviewed");
      await expect(row).not.toContainText("Cancellation requested");
    });

    await test.step("The card still shows the used day", async () => {
      await openMyLeave(page);
      await expectAnnualDays(page, earned - 8);
      await expectAnnualFooter(page, 8, earned);
      await expect(requestRow(page, note)).toContainText("Approved");
    });

    await test.step("The cancellation is rejected and the emails omit the reason", async () => {
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
        email: freshEmployee.email,
      });
      expect(await usedAnnualHours(db, freshEmployee.email, plan.year)).toBe(8);
      expect(await cancellationRows(db, note)).toEqual([
        ["Cancellation", reason, "Rejected", accounts.hr.email, "Approved"],
      ]);
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your leave cancellation was rejected",
        hrSubject: "Leave cancellation rejected",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
      await bodiesOmit(db, freshEmployee.name, reason);
    });
  });

  test("LEAVE-NEG-02 leave that includes today cannot be cancelled", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireEarned(plan);
    const date = startedDate(plan);
    const note = `LEAVE-NEG-02 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("The employee has no cancellation control", async () => {
      await openMyLeave(page);
      const row = requestRow(page, note);
      await expect(row).toContainText("Approved");
      await expect(
        row.getByRole("button", { name: "Request cancellation" }),
      ).toHaveCount(0);
      await expectAnnualDays(page, earned - 8);
    });

    await test.step("A direct request is refused and nothing is stored", async () => {
      const response = await page.request.post("/api/cancellations", {
        data: { request_id: await requestId(db, note), reason: "Too late" },
      });
      expect(response.status()).toBe(400);
      expect(((await response.json()) as { error?: string }).error).toBe(
        startedError,
      );
      expect(await cancellationRows(db, note)).toEqual([]);
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
      });
      expect(await usedAnnualHours(db, freshEmployee.email, plan.year)).toBe(8);
    });
  });

  test("LEAVE-NEG-03 a second cancellation is refused while one is pending", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-NEG-03 ${uniqueTag}`;
    const reason = `Plans changed ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("The employee asks once", async () => {
      await askToCancel(page, note, reason);
      await expectSuccess(page, "Cancellation sent to HR for review.");
      await expect(
        requestRow(page, note).getByRole("button", {
          name: "Request cancellation",
        }),
      ).toHaveCount(0);
    });

    await test.step("A second request is refused and the first row remains", async () => {
      const response = await page.request.post("/api/cancellations", {
        data: {
          request_id: await requestId(db, note),
          reason: "Asking again",
        },
      });
      expect(response.status()).toBe(400);
      expect(((await response.json()) as { error?: string }).error).toBe(
        duplicateError,
      );
      expect(await cancellationRows(db, note)).toEqual([
        ["Cancellation", reason, "Pending", "", "Approved"],
      ]);
    });
  });

  test("LEAVE-VAL-04 a blank reason is not stored", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-VAL-04 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("A blank reason does not send the cancellation", async () => {
      await openMyLeave(page);
      await answerDialogs(page, ["   ", null]);
      const row = requestRow(page, note);
      await row.getByRole("button", { name: "Request cancellation" }).click();
      await expect(
        page.getByRole("status").filter({ hasText: "Cancellation sent" }),
      ).toHaveCount(0);
      await expect(
        page.getByRole("alert").filter({ hasText: blankReasonError }),
      ).toHaveCount(0);
      await expect(row).toContainText("Approved");
      await expect(
        row.getByRole("button", { name: "Request cancellation" }),
      ).toBeVisible();
    });

    await test.step("Dismissing the prompt also writes nothing", async () => {
      await requestRow(page, note)
        .getByRole("button", { name: "Request cancellation" })
        .click();
      await expect(await cancellationRows(db, note)).toEqual([]);
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
      });
    });
  });

  test("LEAVE-VAL-05 a reason over 500 characters is refused", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-VAL-05 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("The long reason is refused", async () => {
      await askToCancel(page, note, "a".repeat(501));
      await expectError(page, blankReasonError);
      await expect(
        page.getByRole("status").filter({ hasText: "Cancellation sent" }),
      ).toHaveCount(0);
      await expect(
        requestRow(page, note).getByRole("button", {
          name: "Request cancellation",
        }),
      ).toBeVisible();
    });

    await test.step("No cancellation row is stored", async () => {
      expect(await cancellationRows(db, note)).toEqual([]);
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
      });
    });
  });

  test("LEAVE-PERM-02 an employee cannot decide a cancellation", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-PERM-02 ${uniqueTag}`;
    const reason = `Plans changed ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("The employee asks to cancel", async () => {
      await askToCancel(page, note, reason);
      await expectSuccess(page, "Cancellation sent to HR for review.");
      const row = requestRow(page, note);
      await expect(
        row.getByRole("button", { name: "Cancel leave" }),
      ).toHaveCount(0);
      await expect(row.getByRole("button", { name: "Keep leave" })).toHaveCount(
        0,
      );
    });

    await test.step("A direct decision is forbidden and the row stays pending", async () => {
      const id = await db.query(
        `SELECT c.id::text
         FROM cancellations c
         JOIN requests r ON r.id = c.request_id
         WHERE r.note = ${sqlString(note)}`,
      );
      const response = await page.request.patch("/api/cancellations", {
        data: { id: Number(id[0][0]), status: "Approved" },
      });
      expect(response.status()).toBe(403);
      expect(((await response.json()) as { error?: string }).error).toBe(
        "Forbidden",
      );
      expect(await cancellationRows(db, note)).toEqual([
        ["Cancellation", reason, "Pending", "", "Approved"],
      ]);
    });
  });

  test("LEAVE-PERM-03 HR cannot cancel a colleague's future annual leave", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    requireEarned(plan);
    const date = futureDate(plan);
    const note = `LEAVE-PERM-03 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await approveLeave(page, hrPage, note, date);

    await test.step("HR approvals has no cancellation control for that day", async () => {
      await openApprovals(hrPage);
      const row = requestRow(hrPage, note);
      await expect(row).toContainText("Approved");
      await expect(row).toContainText("Reviewed");
      await expect(
        row.getByRole("button", { name: "Request cancellation" }),
      ).toHaveCount(0);
      await expect(
        row.getByRole("button", { name: "Correct (cancel) this leave" }),
      ).toHaveCount(0);
      await expect(
        row.getByRole("button", { name: "Cancel leave" }),
      ).toHaveCount(0);
      await expect(row.getByRole("button", { name: "Keep leave" })).toHaveCount(
        0,
      );
    });

    await test.step("A direct request is refused and nothing is stored", async () => {
      const response = await hrPage.request.post("/api/cancellations", {
        data: {
          request_id: await requestId(db, note),
          reason: "HR asked",
        },
      });
      expect(response.status()).toBe(400);
      expect(((await response.json()) as { error?: string }).error).toBe(
        colleagueError,
      );
      expect(await cancellationRows(db, note)).toEqual([]);
    });
  });

  test("LEAVE-PERM-04 HR cannot decide their own cancellation", async ({
    hrPage,
    adminPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const inputs = await employmentInputs(db, accounts.hr.email);
    const earned =
      inputs && inputs.employmentType === "Full-time" && inputs.level
        ? earnedAnnualHours(inputs.joinDate, plan.today, inputs.level)
        : null;
    const used = await usedAnnualHours(db, accounts.hr.email, plan.year);
    const taken = await takenLeaveDates(db, accounts.hr.email);
    const date = plan.weekdays.find(
      (item) => item > plan.today && !taken.has(item),
    );
    test.skip(earned == null, "HR annual leave is pending policy.");
    test.skip(
      (earned ?? 0) - used < 8,
      `HR has ${(earned ?? 0) - used} annual hours left on ${plan.today}.`,
    );
    test.skip(date == null, `HR has no free weekday after ${plan.today}.`);
    const note = `LEAVE-PERM-04 ${uniqueTag}`;
    const reason = `Plans changed ${uniqueTag}`;

    await test.step("HR submits a future day and Admin approves it", async () => {
      await submitLeave(hrPage, { start: date!, end: date!, note });
      await expectSuccess(hrPage, "Leave request saved for HR review.");
      await decide(adminPage, note, "Approve");
      await expectSuccess(adminPage, "Request approved and saved.");
    });

    await test.step("HR asks to cancel their own day", async () => {
      await askToCancel(hrPage, note, reason);
      await expectSuccess(hrPage, "Cancellation sent to HR for review.");
    });

    await test.step("HR sees no decision buttons", async () => {
      await openApprovals(hrPage);
      const row = requestRow(hrPage, note);
      await expect(row).toContainText("Another reviewer decides");
      await expect(
        row.getByRole("button", { name: "Cancel leave" }),
      ).toHaveCount(0);
      await expect(row.getByRole("button", { name: "Keep leave" })).toHaveCount(
        0,
      );
    });

    await test.step("A direct decision is refused and the leave stays approved", async () => {
      const id = await db.query(
        `SELECT c.id::text
         FROM cancellations c
         JOIN requests r ON r.id = c.request_id
         WHERE r.note = ${sqlString(note)}`,
      );
      const response = await hrPage.request.patch("/api/cancellations", {
        data: { id: Number(id[0][0]), status: "Approved" },
      });
      expect(response.status()).toBe(400);
      expect(((await response.json()) as { error?: string }).error).toBe(
        ownDecisionError,
      );
      expect(await cancellationRows(db, note)).toEqual([
        ["Cancellation", reason, "Pending", "", "Approved"],
      ]);
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
        email: accounts.hr.email,
      });
    });
  });
});
