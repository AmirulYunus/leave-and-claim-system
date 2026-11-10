import fs from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import type { FullConfig } from "@playwright/test";
import {
  appPort,
  appUrl,
  root,
  seedPassword,
  stateFile,
} from "./constants.cjs";

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

function commandOutput(command: string, args: string[]) {
  const result = spawnSync(command, args, {
    encoding: "utf8",
    timeout: 20_000,
  });
  return (result.stdout ?? "").replace(/^\uFEFF/, "");
}

function listenerPids(port: number) {
  const text =
    process.platform === "win32"
      ? commandOutput("powershell.exe", [
          "-NoProfile",
          "-Command",
          `(Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue).OwningProcess`,
        ])
      : commandOutput("lsof", [`-iTCP:${port}`, "-sTCP:LISTEN", "-t"]);
  return [
    ...new Set(
      text
        .split(/\s+/)
        .map((part) => Number(part))
        .filter((pid) => Number.isInteger(pid) && pid > 0),
    ),
  ];
}

function parentPid(pid: number) {
  const text =
    process.platform === "win32"
      ? commandOutput("powershell.exe", [
          "-NoProfile",
          "-Command",
          `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}").ParentProcessId`,
        ])
      : commandOutput("ps", ["-o", "ppid=", "-p", String(pid)]);
  const parent = Number(text.trim());
  return Number.isInteger(parent) && parent > 0 ? parent : null;
}

function commandLine(pid: number) {
  if (process.platform === "win32") {
    return commandOutput("powershell.exe", [
      "-NoProfile",
      "-Command",
      `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}").CommandLine`,
    ]);
  }
  return commandOutput("ps", ["-p", String(pid), "-o", "args="]);
}

function alive(pid: number) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function harnessPid() {
  if (!fs.existsSync(stateFile)) return null;
  try {
    const state = JSON.parse(fs.readFileSync(stateFile, "utf8")) as {
      pid?: unknown;
    };
    return typeof state.pid === "number" ? state.pid : null;
  } catch {
    return null;
  }
}

// The process on port 3200 is Next. environment.mjs records its own pid and is the parent.
function harnessIsAncestor(listenerPid: number, ownerPid: number) {
  let current = parentPid(listenerPid);
  for (let depth = 0; depth < 6 && current; depth += 1) {
    if (current === ownerPid) return true;
    current = parentPid(current);
  }
  return false;
}

function assertTestAppIsListening() {
  const ownerPid = harnessPid();
  const listeners = listenerPids(appPort);
  const ownerIsHarness =
    ownerPid != null &&
    alive(ownerPid) &&
    commandLine(ownerPid).includes("environment.mjs") &&
    listeners.length > 0 &&
    listeners.every((pid) => harnessIsAncestor(pid, ownerPid));
  if (ownerIsHarness) return;
  throw new Error(
    [
      "Another program is using port 3200, so the tests did not start.",
      "Close that program. The tests will not close it for you.",
      "If it is a previous test app, run npm run e2e:stop and then run the tests again.",
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
  if (hostname !== "127.0.0.1") {
    throw new Error(
      [
        `Tests only run against 127.0.0.1. The configured address is ${baseURL}.`,
        "Change use.baseURL in playwright.config.ts back to http://127.0.0.1:3200.",
        "The tests did not start.",
      ].join("\n"),
    );
  }
  if (config.workers !== 1) {
    throw new Error(
      [
        `These tests share one database, so they must run one at a time. This run would use ${String(config.workers)} workers.`,
        "Keep workers set to 1 in playwright.config.ts.",
        "The tests did not start.",
      ].join("\n"),
    );
  }
  assertTestAppIsListening();

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
