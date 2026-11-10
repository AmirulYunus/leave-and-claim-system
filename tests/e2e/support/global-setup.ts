import { spawnSync } from "node:child_process";
import path from "node:path";
import type { FullConfig } from "@playwright/test";
import { appUrl, root, seedPassword } from "./constants.cjs";

const script = path.join(root, "tests/e2e/support/environment.mjs");

function testerError(problem: string): never {
  throw new Error(
    [
      problem,
      "",
      "The tests stopped before they could change any data.",
      "This run does not use your own database.",
      "",
      "What to do:",
      "1. Open Docker Desktop and wait until it is running.",
      "2. In this project, run: npm run e2e:stop",
      "3. Run the tests again: npm run test:e2e",
      "",
      "To open the test app yourself, run npm run e2e:env and wait until it prints the accounts.",
      `Then open ${appUrl} and sign in as hr@example.test with password ${seedPassword}.`,
    ].join("\n"),
  );
}

function sessionCount(): number {
  const result = spawnSync(
    process.execPath,
    [script, "sql", "SELECT count(*) FROM sessions"],
    { encoding: "utf8" },
  );
  if (result.status !== 0) {
    testerError(
      "The tests could not read the throwaway database leave-e2e-db.\n" +
        (result.stderr || result.stdout || "").trim(),
    );
  }
  const count = Number(result.stdout.trim());
  if (!Number.isInteger(count)) {
    testerError(
      "The throwaway database answered, but the session count was not a number.",
    );
  }
  return count;
}

export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects[0]?.use.baseURL;
  if (typeof baseURL !== "string") {
    testerError(
      "playwright.config.ts must set use.baseURL to http://127.0.0.1:3200.",
    );
  }
  let hostname = "";
  try {
    hostname = new URL(baseURL).hostname;
  } catch {
    testerError(`The test address ${baseURL} is not a valid URL.`);
  }
  if (!["127.0.0.1", "localhost", "::1"].includes(hostname)) {
    throw new Error(
      [
        `Tests only run against this computer. The configured address is ${baseURL}.`,
        "Change use.baseURL in playwright.config.ts back to http://127.0.0.1:3200.",
        "The tests did not start.",
      ].join("\n"),
    );
  }

  const before = sessionCount();
  let status = 0;
  let detail = "";
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      const response = await fetch(`${baseURL}/api/auth/session`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Origin: baseURL,
        },
        body: JSON.stringify({
          email: "hr@example.test",
          password: seedPassword,
        }),
      });
      status = response.status;
      if (status === 200 || status === 401 || status === 403) break;
      detail = (await response.text()).slice(0, 300);
    } catch (error) {
      detail =
        error instanceof Error ? error.message : "The app did not answer.";
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  if (status !== 200) {
    testerError(
      `Sign-in to the test app failed (HTTP ${status || "no response"}). ${detail}`,
    );
  }
  const after = sessionCount();
  if (after !== before + 1) {
    testerError(
      `The app at ${baseURL} did not write its sign-in to the throwaway database leave-e2e-db (sessions went from ${before} to ${after}). Tests were stopped so they cannot touch another database.`,
    );
  }
}
