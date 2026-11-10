import fs from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { randomBytes } from "node:crypto";
import {
  test as base,
  expect,
  type Browser,
  type Page,
  type TestInfo,
} from "@playwright/test";
import { root, seedPassword } from "./constants.cjs";
import { signIn } from "./app";
import { createTemporaryEmployee } from "./provision";

const script = path.join(root, "tests/e2e/support/environment.mjs");

export type Account = {
  name: string;
  email: string;
  password: string;
  role: "Employee" | "HR" | "Admin";
};

export type Accounts = {
  password: string;
  employee: Account;
  hr: Account;
  admin: Account;
  sarah: Account;
  daniel: Account;
};

export type Database = {
  query: (sql: string) => Promise<string[][]>;
};

export type FreshEmployee = {
  id: number;
  name: string;
  email: string;
  password: string;
  signedInPage: () => Promise<Page>;
};

const account = (
  name: string,
  email: string,
  role: Account["role"],
): Account => ({ name, email, password: seedPassword, role });

const seeded: Accounts = {
  password: seedPassword,
  employee: account("Amirul", "amirul@example.test", "Employee"),
  hr: account("HR", "hr@example.test", "HR"),
  admin: account("System Admin", "admin@example.test", "Admin"),
  sarah: account("Sarah Tan", "sarah@example.test", "Employee"),
  daniel: account("Daniel Lee", "daniel@example.test", "Employee"),
};

function query(sql: string): Promise<string[][]> {
  const result = spawnSync(process.execPath, [script, "sql", sql], {
    encoding: "utf8",
  });
  if (result.status !== 0) {
    throw new Error(
      (result.stderr || result.stdout || "The database query failed.").trim(),
    );
  }
  const text = result.stdout.replace(/\r/g, "").replace(/\n$/, "");
  if (!text) return Promise.resolve([]);
  return Promise.resolve(text.split("\n").map((row) => row.split("\t")));
}

function baseURL(testInfo: TestInfo) {
  const configured = testInfo.project.use.baseURL;
  if (!configured) throw new Error("Set use.baseURL in playwright.config.ts.");
  return configured;
}

async function finishContext(page: Page, testInfo: TestInfo, label: string) {
  const context = page.context();
  const video = page.video();
  const shot = testInfo.outputPath(`${label}.png`);
  await page.screenshot({ path: shot, fullPage: true }).catch(() => undefined);
  await context.close();
  if (fs.existsSync(shot)) {
    await testInfo.attach(`${label}-screenshot`, {
      path: shot,
      contentType: "image/png",
    });
  }
  if (video) {
    const saved = testInfo.outputPath(`${label}.webm`);
    await video.saveAs(saved);
    await testInfo.attach(`${label}-video`, {
      path: saved,
      contentType: "video/webm",
    });
  }
}

async function openSignedInPage(
  browser: Browser,
  actor: { email: string; password: string },
  testInfo: TestInfo,
  label: string,
) {
  const origin = baseURL(testInfo);
  const context = await browser.newContext({
    // trace: 'on' already records this context. Starting it again throws.
    baseURL: origin,
    viewport: { width: 1280, height: 720 },
    recordVideo: { dir: testInfo.outputDir },
  });
  const page = await context.newPage();
  const finish = () => finishContext(page, testInfo, label);
  try {
    await signIn(page, actor.email, actor.password);
    await expect(
      page.getByRole("heading", { name: /Good morning/ }),
    ).toBeVisible();
  } catch (error) {
    await finish();
    throw error;
  }
  return { page, finish };
}

type Fixtures = {
  accounts: Accounts;
  db: Database;
  uniqueTag: string;
  employeePage: Page;
  hrPage: Page;
  adminPage: Page;
  freshEmployee: FreshEmployee;
};

export const test = base.extend<Fixtures>({
  accounts: async ({}, run) => {
    await run(seeded);
  },
  db: async ({}, run) => {
    await run({ query });
  },
  uniqueTag: async ({}, run) => {
    await run(randomBytes(4).toString("hex"));
  },
  employeePage: async ({ browser, accounts }, run, testInfo) => {
    const opened = await openSignedInPage(
      browser,
      accounts.employee,
      testInfo,
      "employee",
    );
    try {
      await run(opened.page);
    } finally {
      await opened.finish();
    }
  },
  hrPage: async ({ browser, accounts }, run, testInfo) => {
    const opened = await openSignedInPage(browser, accounts.hr, testInfo, "hr");
    try {
      await run(opened.page);
    } finally {
      await opened.finish();
    }
  },
  adminPage: async ({ browser, accounts }, run, testInfo) => {
    const opened = await openSignedInPage(
      browser,
      accounts.admin,
      testInfo,
      "admin",
    );
    try {
      await run(opened.page);
    } finally {
      await opened.finish();
    }
  },
  freshEmployee: async ({ browser, accounts, uniqueTag }, run, testInfo) => {
    const created = await createTemporaryEmployee(
      {
        baseURL: baseURL(testInfo),
        email: accounts.hr.email,
        password: accounts.hr.password,
      },
      uniqueTag,
      { changePassword: true },
    );
    let opened: Awaited<ReturnType<typeof openSignedInPage>> | undefined;
    try {
      await run({
        ...created,
        signedInPage: async () => {
          opened ??= await openSignedInPage(
            browser,
            created,
            testInfo,
            "fresh-employee",
          );
          return opened.page;
        },
      });
    } finally {
      if (opened) await opened.finish();
    }
  },
});

export { expect };
