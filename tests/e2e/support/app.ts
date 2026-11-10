import { expect, type Page } from "@playwright/test";

const timeZone = "Asia/Kuala_Lumpur";

export async function signIn(page: Page, email: string, password: string) {
  await page.goto("/");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  const outcome = page
    .getByRole("heading", { name: /Good morning|Choose a new password/ })
    .or(page.getByRole("alert").filter({ hasText: /\S/ }));
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(outcome).toBeVisible();
}

export async function signOut(page: Page) {
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(
    page.getByRole("heading", { name: "Sign in to People & Co." }),
  ).toBeVisible();
}

export async function expectSuccess(page: Page, message: string | RegExp) {
  const banner = page.getByRole("status").filter({ hasText: "✓ " });
  await expect(banner).toContainText(message);
  await expect(banner).toContainText(/^✓ /);
}

export async function expectError(page: Page, message: string | RegExp) {
  await expect(
    page.getByRole("alert").filter({ hasText: message }),
  ).toBeVisible();
}

export async function expectRefusal(page: Page, message: string) {
  await expect(
    page.getByRole("alert").filter({ hasText: message }),
  ).toBeVisible();
  await expect(
    page.getByRole("status").filter({ hasText: message }),
  ).toHaveCount(0);
}

export function answerDialogs(
  page: Page,
  answers: Array<string | null>,
  expectedMessages?: Array<string | RegExp | undefined>,
) {
  const pending = [...answers];
  const expected = expectedMessages ? [...expectedMessages] : [];
  page.on("dialog", async (dialog) => {
    const message = expected.shift();
    if (typeof message === "string") expect(dialog.message()).toBe(message);
    else if (message) expect(dialog.message()).toMatch(message);
    const answer = pending.shift();
    if (answer == null) {
      await dialog.dismiss();
      return;
    }
    await dialog.accept(dialog.type() === "prompt" ? answer : "");
  });
}

export function malaysiaToday(now = new Date()) {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(now);
}

export function addCalendarDays(iso: string, days: number) {
  const [year, month, day] = iso.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day + days))
    .toISOString()
    .slice(0, 10);
}
