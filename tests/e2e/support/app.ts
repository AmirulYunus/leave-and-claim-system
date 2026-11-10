import { expect, type Page } from "@playwright/test";

const timeZone = "Asia/Kuala_Lumpur";

export async function signIn(page: Page, email: string, password: string) {
  await page.goto("/");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  const outcome = page
    .getByRole("heading", { name: /Good morning|Choose a new password/ })
    .or(page.getByRole("alert").filter({ hasText: /\S/ }));
  const button = page.getByRole("button", { name: "Sign in" });
  await button.click();
  const started = await outcome
    .or(page.getByRole("button", { name: "Please wait…" }))
    .waitFor({ state: "visible", timeout: 5_000 })
    .then(() => true)
    .catch(() => false);
  if (!started && (await button.isVisible())) await button.click();
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

export function answerDialogs(page: Page, answers: Array<string | null>) {
  page.on("dialog", async (dialog) => {
    const answer = answers.shift();
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

function weekday(iso: string) {
  const [year, month, day] = iso.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day)).getUTCDay();
}

export function nextWeekdayThisYear(now = new Date()) {
  const today = malaysiaToday(now);
  const year = today.slice(0, 4);
  for (let offset = 1; offset <= 370; offset++) {
    const candidate = addCalendarDays(today, offset);
    if (!candidate.startsWith(year)) break;
    const day = weekday(candidate);
    if (day !== 0 && day !== 6) return candidate;
  }
  for (let offset = 0; offset <= 370; offset++) {
    const candidate = addCalendarDays(today, -offset);
    if (!candidate.startsWith(year)) break;
    const day = weekday(candidate);
    if (day !== 0 && day !== 6) return candidate;
  }
  return today;
}
