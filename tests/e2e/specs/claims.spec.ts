import { type Locator, type Page } from "@playwright/test";
import { test, expect } from "../support/fixtures";
import { expectSuccess } from "../support/app";

const zeroClaimSentence = "Enter a claim amount greater than zero.";
const claimRangeSentence =
  "Enter a claim amount from RM 0.01 to RM 100,000 with at most two decimal places.";

async function expectClaimAmountRefused(
  page: Page,
  dialog: Locator,
  amount: string,
) {
  const field = dialog.getByLabel("Amount (RM)");
  const native = await field.evaluate(
    (element: HTMLInputElement) => element.validationMessage,
  );
  const alert = page.getByRole("alert").filter({
    hasText:
      amount === "0"
        ? zeroClaimSentence
        : new RegExp(`${zeroClaimSentence}|${claimRangeSentence}`),
  });
  if (native) {
    expect(native).toMatch(/0\.01/);
    return;
  }
  await expect(alert).toBeVisible();
}
import {
  claimRequest,
  employeeId,
  expectDecisionMail,
  requestCount,
  sqlString,
} from "../support/records";
import { decide, showDecision, submitClaim } from "../support/ui";

test.describe("Expense claims", () => {
  test("CLAIM-POS-01 a 42.50 claim is saved as pending", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const note = `CLAIM-POS-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit a travel claim", async () => {
      await submitClaim(page, {
        category: "Travel",
        description: "Client taxi",
        amount: "42.50",
        note,
      });
      await expectSuccess(page, "Claim saved for HR review.");
    });

    await test.step("My claims shows the pending amount", async () => {
      await page
        .getByRole("button", { name: "My claims", exact: true })
        .click();
      const row = page.getByRole("row").filter({ hasText: note });
      await expect(row).toContainText("Pending");
      await expect(row).toContainText("RM 42.50");
    });

    await test.step("The claim is stored as pending", async () => {
      expect(await claimRequest(db, note)).toEqual({
        status: "Pending",
        reviewerId: "",
        reviewed: false,
        kind: "Claim",
        category: "Travel",
        detail: "Client taxi",
        amount: 42.5,
        email: freshEmployee.email,
      });
    });
  });

  test("CLAIM-POS-02 HR approval stores Approved and queues claim email", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const note = `CLAIM-POS-02 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();
    const reviewer = await employeeId(db, accounts.hr.email);

    await test.step("Submit a travel claim", async () => {
      await submitClaim(page, {
        category: "Travel",
        description: "Client taxi",
        amount: "42.50",
        note,
      });
      await expectSuccess(page, "Claim saved for HR review.");
    });

    await test.step("HR approves it", async () => {
      await decide(hrPage, note, "Approve");
      await expectSuccess(hrPage, "Request approved and saved.");
      const row = await showDecision(hrPage, note, "Approved");
      await expect(row).toContainText("RM 42.50");
    });

    await test.step("The amount, reviewer, and emails are stored", async () => {
      expect(await claimRequest(db, note)).toMatchObject({
        status: "Approved",
        reviewerId: reviewer,
        reviewed: true,
        amount: 42.5,
        email: freshEmployee.email,
      });
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your claim was approved",
        hrSubject: "Claim approved",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
    });
  });

  test("CLAIM-NEG-01 rejection stores Rejected and keeps the amount", async ({
    freshEmployee,
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const note = `CLAIM-NEG-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();
    const reviewer = await employeeId(db, accounts.hr.email);

    await test.step("Submit the claim HR will reject", async () => {
      await submitClaim(page, {
        category: "Travel",
        description: "Rejected fare",
        amount: "18.00",
        note,
      });
      await expectSuccess(page, "Claim saved for HR review.");
    });

    await test.step("HR rejects it", async () => {
      await decide(hrPage, note, "Reject");
      await expectSuccess(hrPage, "Request rejected and saved.");
      const row = await showDecision(hrPage, note, "Rejected");
      await expect(row).toContainText("RM 18.00");
    });

    await test.step("The amount stays 18.00", async () => {
      expect(await claimRequest(db, note)).toMatchObject({
        status: "Rejected",
        reviewerId: reviewer,
        reviewed: true,
        amount: 18,
        detail: "Rejected fare",
      });
      await expectDecisionMail(db, {
        email: freshEmployee.email,
        name: freshEmployee.name,
        employeeSubject: "Your claim was rejected",
        hrSubject: "Claim rejected",
        hrEmail: accounts.hr.email,
        adminEmail: accounts.admin.email,
      });
    });
  });

  test("CLAIM-VAL-01 one cent is accepted", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const note = `CLAIM-VAL-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit a one-cent claim", async () => {
      await submitClaim(page, {
        category: "Other",
        description: "One cent",
        amount: "0.01",
        note,
      });
      await expectSuccess(page, "Claim saved for HR review.");
      await page
        .getByRole("button", { name: "My claims", exact: true })
        .click();
      const row = page.getByRole("row").filter({ hasText: note });
      await expect(row).toContainText("Pending");
      await expect(row).toContainText("RM 0.01");
    });

    await test.step("One cent is stored", async () => {
      expect(await claimRequest(db, note)).toMatchObject({
        status: "Pending",
        amount: 0.01,
        category: "Other",
      });
    });
  });

  test("CLAIM-VAL-02 zero and a fraction of a cent are not stored", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const note = `CLAIM-VAL-02 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Try zero, then a fraction of a cent", async () => {
      const dialog = await submitClaim(page, {
        category: "Other",
        description: note,
        amount: "0",
        note,
      });
      await expect(
        dialog.getByRole("heading", { name: "Submit an expense claim." }),
      ).toBeVisible();
      await expectClaimAmountRefused(page, dialog, "0");
      await expect(
        page.getByRole("status").filter({ hasText: "✓ " }),
      ).toHaveCount(0);
      await dialog.getByLabel("Amount (RM)").fill("0.001");
      await dialog.getByRole("button", { name: "Submit for approval" }).click();
      await expect(
        dialog.getByRole("heading", { name: "Submit an expense claim." }),
      ).toBeVisible();
      await expectClaimAmountRefused(page, dialog, "0.001");
      await expect(
        page.getByRole("status").filter({ hasText: "✓ " }),
      ).toHaveCount(0);
    });

    await test.step("No claim was stored", async () => {
      expect(await requestCount(db, note)).toBe(0);
    });
  });

  test("CLAIM-PERM-01 HR cannot decide their own claim", async ({
    hrPage,
    accounts,
    db,
    uniqueTag,
  }) => {
    const note = `CLAIM-PERM-01 ${uniqueTag}`;
    const beforeMail = await db.query(
      `SELECT count(*)::text FROM notifications`,
    );

    await test.step("HR submits their own claim", async () => {
      await submitClaim(hrPage, {
        category: "Meals",
        description: "Team lunch",
        amount: "15.00",
        note,
      });
      await expectSuccess(hrPage, "Claim saved for HR review.");
    });

    await test.step("The row has no decision buttons", async () => {
      await hrPage.getByRole("button", { name: /HR approvals/ }).click();
      const row = hrPage.getByRole("row").filter({ hasText: note });
      await expect(row).toContainText("Another reviewer decides");
      await expect(row).toContainText("Pending");
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

    await test.step("The claim stays pending", async () => {
      expect(await claimRequest(db, note)).toMatchObject({
        status: "Pending",
        reviewerId: "",
        reviewed: false,
        amount: 15,
        email: accounts.hr.email,
      });
      expect(
        await db.query(`SELECT count(*)::text FROM notifications`),
      ).toEqual(beforeMail);
    });
  });
});
