import { test, expect } from "../support/fixtures";
import { expectError, expectSuccess } from "../support/app";
import {
  attachmentCount,
  employeeId,
  employmentInputs,
  expectDecisionMail,
  leaveEntries,
  leaveRequest,
  notificationSubjects,
  requestCount,
  sqlString,
  takenLeaveDates,
  usedAnnualHours,
} from "../support/records";
import {
  earnedAnnualHours,
  freshJoin,
  leaveAmount,
  leaveSummary,
  medicalWeekday,
  overBalanceSpan,
  yearPlan,
  type YearPlan,
} from "../support/rules";
import {
  decide,
  expectAnnualDays,
  expectAnnualFooter,
  requestRow,
  showDecision,
  submitLeave,
} from "../support/ui";

function requireHours(plan: YearPlan, hours: number) {
  test.skip(
    plan.earnedHours == null,
    `Annual leave is pending policy on ${plan.today} for a level 1 employee who joined ${freshJoin}.`,
  );
  test.skip(
    (plan.earnedHours ?? 0) < hours,
    `Only ${plan.earnedHours} annual hours are earned on ${plan.today}; this case needs ${hours}.`,
  );
  test.skip(
    plan.weekdays.length === 0,
    `No weekday after 31 March ${plan.year}.`,
  );
  return plan.earnedHours ?? 0;
}

test.describe("Leave requests", () => {
  test("LEAVE-POS-01 a full weekday stays pending and the balance card is unchanged", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireHours(plan, 8);
    const date = plan.weekdays[0];
    const note = `LEAVE-POS-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("The card shows the earned balance", async () => {
      await expectAnnualDays(page, earned);
    });

    await test.step("Submit one full weekday", async () => {
      await submitLeave(page, {
        start: date,
        end: date,
        note,
        summary: leaveSummary(1, 8),
      });
      await expectSuccess(page, "Leave request saved for HR review.");
      await expect(requestRow(page, note)).toContainText("Pending");
      await expect(requestRow(page, note)).toContainText(leaveAmount(8));
      await expectAnnualDays(page, earned);
    });

    await test.step("The request is pending, with no reviewer and no email", async () => {
      expect(await leaveRequest(db, note)).toEqual({
        status: "Pending",
        reviewerId: "",
        reviewed: false,
        kind: "Leave",
        category: "Annual leave",
        carryHours: 0,
        email: freshEmployee.email,
      });
      expect(await leaveEntries(db, note)).toEqual([[date, 8]]);
      expect(
        await notificationSubjects(db, freshEmployee.email, freshEmployee.name),
      ).toEqual([]);
    });
  });

  test("LEAVE-POS-02 HR approval names the reviewer, drops the card, and queues email", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireHours(plan, 8);
    const date = plan.weekdays[0];
    const note = `LEAVE-POS-02 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();
    const reviewer = await employeeId(db, accounts.hr.email);

    await test.step("Submit one full weekday", async () => {
      await submitLeave(page, { start: date, end: date, note });
      await expectSuccess(page, "Leave request saved for HR review.");
    });

    await test.step("HR approves it", async () => {
      await decide(hrPage, note, "Approve");
      await expectSuccess(hrPage, "Request approved and saved.");
      await showDecision(hrPage, note, "Approved");
    });

    await test.step("The employee's card drops by one day", async () => {
      await page.reload();
      await expectAnnualDays(page, earned - 8);
      await expectAnnualFooter(page, 8, earned);
    });

    await test.step("The decision, the day, and the two emails are stored", async () => {
      expect(await leaveRequest(db, note)).toEqual({
        status: "Approved",
        reviewerId: reviewer,
        reviewed: true,
        kind: "Leave",
        category: "Annual leave",
        carryHours: 0,
        email: freshEmployee.email,
      });
      expect(await leaveEntries(db, note)).toEqual([[date, 8]]);
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your leave request was approved",
        hrSubject: "Leave request approved",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
    });
  });

  test("LEAVE-POS-03 rejection leaves the balance card unchanged", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireHours(plan, 8);
    const date = plan.weekdays[0];
    const note = `LEAVE-POS-03 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();
    const reviewer = await employeeId(db, accounts.hr.email);

    await test.step("Submit one full weekday", async () => {
      await submitLeave(page, { start: date, end: date, note });
      await expectSuccess(page, "Leave request saved for HR review.");
      await expectAnnualDays(page, earned);
    });

    await test.step("HR rejects it", async () => {
      await decide(hrPage, note, "Reject");
      await expectSuccess(hrPage, "Request rejected and saved.");
      await showDecision(hrPage, note, "Rejected");
    });

    await test.step("The card is unchanged", async () => {
      await page.reload();
      await expectAnnualDays(page, earned);
    });

    await test.step("The rejection is stored and the day remains", async () => {
      expect(await leaveRequest(db, note)).toEqual({
        status: "Rejected",
        reviewerId: reviewer,
        reviewed: true,
        kind: "Leave",
        category: "Annual leave",
        carryHours: 0,
        email: freshEmployee.email,
      });
      expect(await leaveEntries(db, note)).toEqual([[date, 8]]);
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your leave request was rejected",
        hrSubject: "Leave request rejected",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
    });
  });

  test("LEAVE-POS-04 a half day stores 4 hours and drops the card by half a day", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireHours(plan, 4);
    const date = plan.weekdays[0];
    const note = `LEAVE-POS-04 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();
    const reviewer = await employeeId(db, accounts.hr.email);

    await test.step("Submit a half day", async () => {
      await expectAnnualDays(page, earned);
      await submitLeave(page, {
        start: date,
        end: date,
        note,
        hours: 4,
        summary: leaveSummary(1, 4),
      });
      await expectSuccess(page, "Leave request saved for HR review.");
      await expect(requestRow(page, note)).toContainText(leaveAmount(4));
      await expectAnnualDays(page, earned);
    });

    await test.step("HR approves it", async () => {
      await decide(hrPage, note, "Approve");
      await expectSuccess(hrPage, "Request approved and saved.");
    });

    await test.step("The card drops by half a day", async () => {
      await page.reload();
      await expectAnnualDays(page, earned - 4);
    });

    await test.step("Four hours are stored against the HR reviewer", async () => {
      expect(await leaveEntries(db, note)).toEqual([[date, 4]]);
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Approved",
        reviewerId: reviewer,
        carryHours: 0,
      });
    });
  });

  test("LEAVE-POS-05 a Friday through Monday stores only the two weekdays", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireHours(plan, 16);
    const span = plan.fridayToMonday;
    test.skip(
      span == null,
      `No Friday-to-Monday span after 31 March ${plan.year}.`,
    );
    const note = `LEAVE-POS-05 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit Friday through Monday", async () => {
      await submitLeave(page, {
        start: span!.start,
        end: span!.end,
        note,
        summary: leaveSummary(2, 8),
      });
      await expectSuccess(page, "Leave request saved for HR review.");
      await expect(requestRow(page, note)).toContainText("Pending");
      await expect(requestRow(page, note)).toContainText(leaveAmount(16));
      await expectAnnualDays(page, earned);
    });

    await test.step("Saturday and Sunday are not stored", async () => {
      expect(await leaveEntries(db, note)).toEqual([
        [span!.start, 8],
        [span!.end, 8],
      ]);
    });
  });

  test("LEAVE-NEG-01 weekend dates are refused", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    test.skip(
      plan.earnedHours == null,
      `Annual leave is pending policy on ${plan.today}.`,
    );
    test.skip(plan.weekend == null, `No weekend after 31 March ${plan.year}.`);
    const note = `LEAVE-NEG-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit Saturday through Sunday", async () => {
      const dialog = await submitLeave(page, {
        start: plan.weekend!.start,
        end: plan.weekend!.end,
        note,
      });
      await expect(dialog.getByText(/× \d+ hours/)).toHaveCount(0);
      await expectError(
        page,
        "Choose at least one eligible date. Date ranges must be within one year.",
      );
      await expect(
        dialog.getByRole("heading", { name: "Make time for yourself." }),
      ).toBeVisible();
    });

    await test.step("Nothing was stored", async () => {
      expect(await requestCount(db, note)).toBe(0);
    });
  });

  test("LEAVE-VAL-01 a date in the next year is refused", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const note = `LEAVE-VAL-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit a date in the next calendar year", async () => {
      const dialog = await submitLeave(page, {
        start: plan.nextYearDate,
        end: plan.nextYearDate,
        note,
      });
      await expectError(
        page,
        `Leave can be requested for ${plan.year} dates only.`,
      );
      await expect(
        dialog.getByRole("heading", { name: "Make time for yourself." }),
      ).toBeVisible();
    });

    await test.step("Nothing was stored", async () => {
      expect(await requestCount(db, note)).toBe(0);
    });
  });

  test("LEAVE-VAL-02 more than earned annual leave is refused", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const earned = requireHours(plan, 0);
    const span = overBalanceSpan(plan);
    test.skip(
      span == null,
      `No range after 31 March ${plan.year} exceeds the earned balance.`,
    );
    const note = `LEAVE-VAL-02 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit more weekdays than the earned balance", async () => {
      const dialog = await submitLeave(page, {
        start: span!.start,
        end: span!.end,
        note,
        summary: leaveSummary(span!.weekdays, 8),
      });
      await expectError(
        page,
        "This request exceeds your balance after pending requests.",
      );
      await expect(
        dialog.getByRole("heading", { name: "Make time for yourself." }),
      ).toBeVisible();
      await expectAnnualDays(page, earned);
    });

    await test.step("Nothing was stored", async () => {
      expect(await requestCount(db, note)).toBe(0);
    });
  });

  test("LEAVE-PERM-01 HR cannot decide their own leave", async ({
    hrPage,
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
    const date = plan.weekdays.find((item) => !taken.has(item));
    test.skip(earned == null, "HR annual leave is pending policy.");
    test.skip(
      (earned ?? 0) - used < 8,
      `HR has ${(earned ?? 0) - used} annual hours left on ${plan.today}.`,
    );
    test.skip(
      date == null,
      `HR already has leave on every weekday after 31 March ${plan.year}.`,
    );
    const note = `LEAVE-PERM-01 ${uniqueTag}`;
    const beforeMail = await db.query(
      `SELECT count(*)::text FROM notifications`,
    );

    await test.step("HR submits their own leave", async () => {
      await submitLeave(hrPage, { start: date!, end: date!, note });
      await expectSuccess(hrPage, "Leave request saved for HR review.");
    });

    await test.step("The row has no decision buttons", async () => {
      await hrPage.getByRole("button", { name: /HR approvals/ }).click();
      const row = requestRow(hrPage, note);
      await expect(row).toContainText("Another reviewer decides");
      await expect(row).toContainText("Pending");
      await expect(row.getByRole("button", { name: "Approve" })).toHaveCount(0);
      await expect(row.getByRole("button", { name: "Reject" })).toHaveCount(0);
    });

    await test.step("A direct approval is refused", async () => {
      const id = await db.query(
        `SELECT id::text FROM requests WHERE note = ${sqlString(note)}`,
      );
      const response = await hrPage.request.patch("/api/workspace", {
        data: { id: Number(id[0][0]), status: "Approved" },
      });
      expect(response.status()).toBe(403);
      expect(((await response.json()) as { error?: string }).error).toBe(
        "You cannot decide your own request. Another HR member or Admin must review it.",
      );
    });

    await test.step("The request stays pending and no email is queued", async () => {
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Pending",
        reviewerId: "",
        reviewed: false,
        email: accounts.hr.email,
      });
      expect(
        await db.query(`SELECT count(*)::text FROM notifications`),
      ).toEqual(beforeMail);
    });
  });

  test("LEAVE-VAL-03 medical leave without a certificate cannot be approved", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    const date = medicalWeekday(plan.today);
    test.skip(
      date == null,
      `No weekday in ${plan.year} is still inside the 48-hour medical window.`,
    );
    const earned = plan.earnedHours;
    const note = `LEAVE-VAL-03 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit medical leave without a certificate", async () => {
      await submitLeave(page, {
        start: date!,
        end: date!,
        note,
        category: "Medical leave",
        help: /HR cannot approve without it/,
        summary: leaveSummary(1, 8),
      });
      await expectSuccess(page, "Leave request saved for HR review.");
      await expect(requestRow(page, note)).toContainText("Pending");
    });

    await test.step("HR cannot approve it", async () => {
      await decide(hrPage, note, "Approve");
      await expectError(
        hrPage,
        "Upload a medical certificate before Medical leave can be approved. HR must verify the certificate.",
      );
      await expect(requestRow(hrPage, note)).toContainText("Pending");
    });

    await test.step("The request stays pending and annual leave is untouched", async () => {
      expect(await leaveRequest(db, note)).toMatchObject({
        status: "Pending",
        reviewerId: "",
        reviewed: false,
      });
      expect(await attachmentCount(db, note)).toBe(0);
      expect(
        await notificationSubjects(db, freshEmployee.email, freshEmployee.name),
      ).toEqual([]);
      if (earned != null) {
        await page.reload();
        await expectAnnualDays(page, earned);
      }
    });
  });
});
