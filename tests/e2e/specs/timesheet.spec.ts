import { test, expect } from "../support/fixtures";
import { expectRefusal, expectSuccess, malaysiaToday } from "../support/app";
import { leaveEntries, sqlString, timesheetHours } from "../support/records";
import {
  freshJoin,
  shiftMonth,
  weekdaysInMonth,
  yearPlan,
} from "../support/rules";
import {
  decide,
  hoursField,
  openTimesheet,
  submitLeave,
  timesheetRow,
} from "../support/ui";

function flagBannerBug(sentence: string, line: number) {
  test.fail();
  test.info().annotations.push({
    type: "app-bug",
    description: `The status banner prefixes "✓ " onto the refusal "${sentence}" (app/page.tsx:1021 renders the message set at app/page.tsx:${line}).`,
  });
}

function monthDay() {
  const today = malaysiaToday();
  const month = today.slice(0, 7);
  const dates = weekdaysInMonth(month);
  test.skip(dates.length === 0, `No weekday in ${month}.`);
  return { today, month, date: dates[0] };
}

test.describe("Timesheet", () => {
  test("TS-POS-01 eight hours are stored for one weekday", async ({
    freshEmployee,
    db,
  }) => {
    const { month, date } = monthDay();
    const page = await freshEmployee.signedInPage();

    await test.step("Enter and save eight hours", async () => {
      await openTimesheet(page, month);
      await hoursField(page, date).fill("8");
      await page.getByRole("button", { name: "Save timesheet" }).click();
      await expectSuccess(page, "Timesheet saved to the database.");
      await expect(
        page.getByRole("button", { name: "Timesheet saved" }),
      ).toBeDisabled();
      await expect(timesheetRow(page, date)).toContainText("Complete");
    });

    await test.step("The hours are stored and no leave was written", async () => {
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([8]);
      const leave = await db.query(
        `SELECT count(*)::text
         FROM leave_entries le
         JOIN requests r ON r.id = le.request_id
         JOIN employees e ON e.id = r.employee_id
         WHERE e.email = ${sqlString(freshEmployee.email)}`,
      );
      expect(leave[0][0]).toBe("0");
    });
  });

  test("TS-VAL-01 a value above 24 is not stored", async ({
    freshEmployee,
    db,
  }) => {
    const { month, date } = monthDay();
    const page = await freshEmployee.signedInPage();

    await test.step("Enter 25 hours", async () => {
      await openTimesheet(page, month);
      await hoursField(page, date).fill("25");
      await expect(hoursField(page, date)).toHaveValue("");
      await expect(
        page.getByRole("button", { name: "Timesheet saved" }),
      ).toBeDisabled();
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([]);
      flagBannerBug("Worked hours must be between 0 and 24.", 1302);
      await expectRefusal(page, "Worked hours must be between 0 and 24.");
    });
  });

  test("TS-NEG-01 worked hours cannot be added on top of 8 approved leave hours", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    test.skip(
      plan.earnedHours == null,
      `Annual leave is pending policy on ${plan.today} for a level 1 employee who joined ${freshJoin}.`,
    );
    test.skip(
      (plan.earnedHours ?? 0) < 8,
      `Only ${plan.earnedHours} annual hours are earned on ${plan.today}.`,
    );
    test.skip(
      plan.weekdays.length === 0,
      `No weekday after 31 March ${plan.year}.`,
    );
    const date = plan.weekdays[0];
    const note = `TS-NEG-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Approve a full day of leave", async () => {
      await submitLeave(page, { start: date, end: date, note });
      await expectSuccess(page, "Leave request saved for HR review.");
      await decide(hrPage, note, "Approve");
      await expectSuccess(hrPage, "Request approved and saved.");
    });

    await test.step("One worked hour on that date is refused", async () => {
      await openTimesheet(page, date.slice(0, 7));
      await expect(timesheetRow(page, date)).toContainText("8 h");
      await hoursField(page, date).fill("1");
      await expect(hoursField(page, date)).toHaveValue("");
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([]);
      expect(await leaveEntries(db, note)).toEqual([[date, 8]]);
      flagBannerBug(
        "Worked hours plus approved leave cannot exceed 8 hours on this date.",
        1308,
      );
      await expectRefusal(
        page,
        "Worked hours plus approved leave cannot exceed 8 hours on this date.",
      );
    });
  });

  test("TS-POS-02 nine hours are stored and marked for review", async ({
    freshEmployee,
    db,
  }) => {
    const { month, date } = monthDay();
    const page = await freshEmployee.signedInPage();

    await test.step("Save nine hours", async () => {
      await openTimesheet(page, month);
      await hoursField(page, date).fill("9");
      await page.getByRole("button", { name: "Save timesheet" }).click();
      await expectSuccess(page, "Timesheet saved to the database.");
      const row = timesheetRow(page, date);
      await expect(row).toContainText("Review extra hours");
      await expect(row).toContainText("9 / 8 h");
    });

    await test.step("Nine hours are stored", async () => {
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([9]);
    });
  });

  test("TS-NEG-02 a blank value deletes that date", async ({
    freshEmployee,
    db,
  }) => {
    const { month, date } = monthDay();
    const page = await freshEmployee.signedInPage();

    await test.step("Save eight hours, then clear the day", async () => {
      await openTimesheet(page, month);
      await hoursField(page, date).fill("8");
      await page.getByRole("button", { name: "Save timesheet" }).click();
      await expectSuccess(page, "Timesheet saved to the database.");
      await hoursField(page, date).fill("");
      await page.getByRole("button", { name: "Save timesheet" }).click();
      await expectSuccess(page, "Timesheet saved to the database.");
    });

    await test.step("The timesheet row is gone", async () => {
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([]);
    });
  });

  test("TS-NEG-03 unsaved edits block sign-out and the month control", async ({
    freshEmployee,
    db,
  }) => {
    const { month, date } = monthDay();
    const page = await freshEmployee.signedInPage();

    await test.step("Enter hours without saving", async () => {
      await openTimesheet(page, month);
      await hoursField(page, date).fill("8");
      const signOut = page.getByRole("button", { name: "Sign out" });
      await expect(signOut).toBeDisabled();
      await expect(signOut).toHaveAttribute(
        "title",
        "Save your timesheet first.",
      );
    });

    await test.step("Changing the month is refused", async () => {
      await page.getByLabel("Timesheet month").fill(shiftMonth(month));
      await expect(page.getByLabel("Timesheet month")).toHaveValue(month);
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([]);
      flagBannerBug("Save the current month before changing months.", 1216);
      await expectRefusal(
        page,
        "Save the current month before changing months.",
      );
    });
  });

  test("TS-POS-03 approved leave is shown and is not written as worked hours", async ({
    freshEmployee,
    hrPage,
    db,
    uniqueTag,
  }) => {
    const plan = yearPlan();
    test.skip(
      plan.earnedHours == null,
      `Annual leave is pending policy on ${plan.today}.`,
    );
    test.skip(
      (plan.earnedHours ?? 0) < 8,
      `Only ${plan.earnedHours} annual hours are earned.`,
    );
    test.skip(
      plan.weekdays.length === 0,
      `No weekday after 31 March ${plan.year}.`,
    );
    const date = plan.weekdays[0];
    const note = `TS-POS-03 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Approve a full day of leave", async () => {
      await submitLeave(page, { start: date, end: date, note });
      await expectSuccess(page, "Leave request saved for HR review.");
      await decide(hrPage, note, "Approve");
      await expectSuccess(hrPage, "Request approved and saved.");
    });

    await test.step("The timesheet shows the leave separately", async () => {
      await openTimesheet(page, date.slice(0, 7));
      await expect(
        page.getByText("Approved leave is added automatically"),
      ).toBeVisible();
      await expect(timesheetRow(page, date)).toContainText("8 h");
      await expect(hoursField(page, date)).toHaveValue("");
    });

    await test.step("Saving zero worked hours keeps the leave at 8", async () => {
      await hoursField(page, date).fill("0");
      await page.getByRole("button", { name: "Save timesheet" }).click();
      await expectSuccess(page, "Timesheet saved to the database.");
      await expect(hoursField(page, date)).toHaveValue("0");
      await expect(timesheetRow(page, date)).toContainText("8 / 8 h");
      expect(await timesheetHours(db, freshEmployee.email, date)).toEqual([0]);
      expect(await leaveEntries(db, note)).toEqual([[date, 8]]);
    });
  });
});
