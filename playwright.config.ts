import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "tests/e2e/specs",
  timeout: 90_000,
  expect: { timeout: 20_000 },
  // The suite shares one throwaway database, so workers must not run at the same time.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  reporter: [
    ["list"],
    ["html", { outputFolder: "playwright-report", open: "never" }],
    ["json", { outputFile: "test-results/results.json" }],
  ],
  outputDir: "test-results",
  globalSetup: "./tests/e2e/support/global-setup.ts",
  globalTeardown: "./tests/e2e/support/global-teardown.ts",
  use: {
    baseURL: "http://127.0.0.1:3200",
    video: "on",
    screenshot: "on",
    trace: "on",
    actionTimeout: 15_000,
    navigationTimeout: 60_000,
    viewport: { width: 1280, height: 720 },
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: "node tests/e2e/support/environment.mjs serve",
    url: "http://127.0.0.1:3200",
    reuseExistingServer: true,
    timeout: 240_000,
    env: { E2E_OWNER: "playwright" },
  },
});
