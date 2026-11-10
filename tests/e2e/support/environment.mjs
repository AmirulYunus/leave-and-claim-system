#!/usr/bin/env node
import { createRequire } from "node:module";
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import path from "node:path";

const {
  appPort,
  appUrl,
  container,
  databaseUrl,
  dbPort,
  distDir,
  root,
  runDir,
  seedPassword,
  stateFile,
  uploadDir,
} = createRequire(import.meta.url)("./constants.cjs");

const windows = process.platform === "win32";
const pathKey =
  Object.keys(process.env).find((key) => key.toUpperCase() === "PATH") ??
  "PATH";
const callerDatabaseUrl = process.env.DATABASE_URL;

function childEnv() {
  const env = { ...process.env };
  for (const key of Object.keys(env)) {
    if (key.toUpperCase() === "DATABASE_URL") delete env[key];
  }
  env.DATABASE_URL = databaseUrl;
  env.SEED_PASSWORD = seedPassword;
  env.UPLOAD_DIR = uploadDir;
  env.EMAIL_DRIVER = "log";
  env.APP_URL = appUrl;
  env.NEXT_DIST_DIR = distDir;
  env[pathKey] = [path.join(root, "node_modules", ".bin"), env[pathKey]]
    .filter(Boolean)
    .join(path.delimiter);
  return env;
}

const env = childEnv();
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const prismaBin = path.join(
  root,
  "node_modules",
  "prisma",
  "build",
  "index.js",
);
const nextBin = path.join(root, "node_modules", "next", "dist", "bin", "next");

function fail(message) {
  console.error(message);
  process.exit(1);
}

function run(command, args) {
  return spawnSync(command, args, { cwd: root, env, encoding: "utf8" });
}

function readState() {
  if (!fs.existsSync(stateFile)) return null;
  return JSON.parse(fs.readFileSync(stateFile, "utf8"));
}

function alive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function commandLine(pid) {
  if (!windows) {
    const result = spawnSync("ps", ["-p", String(pid), "-o", "args="], {
      encoding: "utf8",
    });
    return result.stdout ?? "";
  }
  const result = spawnSync(
    "powershell.exe",
    [
      "-NoProfile",
      "-Command",
      `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}").CommandLine`,
    ],
    { encoding: "utf8" },
  );
  return result.stdout ?? "";
}

function ourServer(pid) {
  return alive(pid) && commandLine(pid).includes("environment.mjs");
}

function removeContainer() {
  run("docker", ["rm", "-f", container]);
}

function sql(query) {
  const result = run("docker", [
    "exec",
    container,
    "psql",
    "-h",
    "127.0.0.1",
    "-U",
    "leave_app",
    "-d",
    "leave_and_claim",
    "-tA",
    "-F",
    "\t",
    "-c",
    query,
  ]);
  if (result.status !== 0)
    throw new Error((result.stderr || result.stdout || "psql failed").trim());
  return result.stdout.replace(/\r/g, "").replace(/\n$/, "");
}

function portFree(port) {
  return new Promise((resolve) => {
    const server = net.createServer();
    server.once("error", () => resolve(false));
    server.listen(port, "127.0.0.1", () => server.close(() => resolve(true)));
  });
}

function assertIsolatedDatabase() {
  if (env.DATABASE_URL !== databaseUrl || !databaseUrl.includes(":55433/"))
    fail(
      "The test runner refused to start because it could not pin the throwaway database on 127.0.0.1:55433.",
    );
  if (callerDatabaseUrl && env.DATABASE_URL === callerDatabaseUrl)
    fail(
      "The test runner refused to start because it would have used DATABASE_URL from the shell. Run npm run e2e:stop, then npm run test:e2e.",
    );
}

async function waitForDatabase() {
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      if (sql("SELECT 1") === "1") return;
    } catch {
      await sleep(1000);
    }
  }
  removeContainer();
  fail(
    [
      "The throwaway test database did not start.",
      "Open Docker Desktop, wait until it is running, then run npm run e2e:stop and npm run test:e2e.",
    ].join("\n"),
  );
}

async function serve() {
  assertIsolatedDatabase();
  const state = readState();
  if (state?.pid && ourServer(state.pid)) {
    fail(
      [
        `The test app is already running at ${appUrl}.`,
        "Leave it running and run npm run test:e2e, or stop it with npm run e2e:stop.",
      ].join("\n"),
    );
  }
  if (!(await portFree(appPort))) {
    fail(
      [
        "Port 3200 is already in use, so the test app cannot start.",
        "If a previous test app is stuck, run npm run e2e:stop and try again.",
      ].join("\n"),
    );
  }
  if (run("docker", ["version"]).status !== 0) {
    fail(
      "Docker Desktop is not running. Open Docker Desktop, wait until it is running, then run npm run test:e2e again.",
    );
  }
  removeContainer();
  if (!(await portFree(dbPort))) {
    fail(
      "Port 55433 is already in use, so the throwaway test database cannot start. Free that port, then run npm run test:e2e again.",
    );
  }

  fs.mkdirSync(uploadDir, { recursive: true });
  fs.mkdirSync(runDir, { recursive: true });
  const started = run("docker", [
    "run",
    "-d",
    "--rm",
    "--name",
    container,
    "-e",
    "POSTGRES_USER=leave_app",
    "-e",
    "POSTGRES_PASSWORD=e2e_only_db",
    "-e",
    "POSTGRES_DB=leave_and_claim",
    "-p",
    `127.0.0.1:${dbPort}:5432`,
    "postgres:17",
  ]);
  if (started.status !== 0) {
    fail(
      [
        "Could not start the throwaway test database.",
        "Open Docker Desktop, wait until it is running, then run npm run e2e:stop and npm run test:e2e.",
        started.stderr.trim(),
      ].join("\n"),
    );
  }
  const owner =
    process.env.E2E_OWNER === "playwright" ? "playwright" : "standalone";
  fs.writeFileSync(
    stateFile,
    JSON.stringify({ container, owner, pid: process.pid, appUrl }, null, 2),
  );

  // Playwright starts this process before globalSetup, so the database has to be ready before next listens.
  await waitForDatabase();
  for (const args of [
    ["migrate", "deploy"],
    ["db", "seed"],
  ]) {
    const result = run(process.execPath, [prismaBin, ...args]);
    if (result.status !== 0) {
      removeContainer();
      fs.rmSync(runDir, { recursive: true, force: true });
      fail(
        [
          "Could not prepare the throwaway test database.",
          "Run npm run e2e:stop, then npm run test:e2e.",
          result.stdout.trim(),
          result.stderr.trim(),
        ]
          .filter(Boolean)
          .join("\n"),
      );
    }
  }

  console.log(`Test database ${container} is ready.`);
  console.log(
    "Accounts: hr@example.test (HR), amirul@example.test (Employee), admin@example.test (Admin), sarah@example.test, daniel@example.test",
  );
  console.log(`Password: ${seedPassword}`);
  console.log(`App: ${appUrl}`);

  nextChild = spawn(
    process.execPath,
    [nextBin, "dev", "--port", String(appPort), "--hostname", "127.0.0.1"],
    { cwd: root, env, stdio: "inherit", windowsHide: true },
  );
  const next = nextChild;
  const exited = new Promise((resolve, reject) => {
    next.once("error", reject);
    next.once("exit", (code) => resolve(code ?? 1));
  });
  announceReady(exited);
  process.exitCode = await exited;
}

async function announceReady(exited) {
  let done = false;
  exited.finally(() => (done = true)).catch(() => {});
  while (!done) {
    try {
      if ((await fetch(appUrl)).ok) {
        console.log(`READY ${appUrl}`);
        return;
      }
    } catch {}
    await sleep(1000);
  }
}

function stop() {
  const state = readState();
  if (state?.pid && ourServer(state.pid)) {
    if (windows) run("taskkill", ["/PID", String(state.pid), "/T", "/F"]);
    else {
      try {
        process.kill(-state.pid, "SIGTERM");
      } catch {
        process.kill(state.pid, "SIGTERM");
      }
    }
  }
  removeContainer();
  fs.rmSync(path.join(root, ".e2e"), { recursive: true, force: true });
  console.log(`Stopped the test app and removed ${container}.`);
}

let nextChild = null;
let closing = false;
function closeStandalone() {
  if (closing || process.env.E2E_OWNER === "playwright") return;
  if (process.env.E2E_KEEP === "1") return;
  closing = true;
  if (nextChild?.pid) {
    if (windows) run("taskkill", ["/PID", String(nextChild.pid), "/T", "/F"]);
    else nextChild.kill("SIGTERM");
  }
  removeContainer();
  fs.rmSync(runDir, { recursive: true, force: true });
}

const [command, ...rest] = process.argv.slice(2);
const commands = {
  serve,
  stop,
  sql: () => {
    const query = rest.join(" ").trim();
    if (!query)
      fail('Usage: node tests/e2e/support/environment.mjs sql "<query>"');
    console.log(sql(query));
  },
};

if (!commands[command])
  fail(
    `Usage: node tests/e2e/support/environment.mjs ${Object.keys(commands).join("|")}`,
  );

if (command === "serve") {
  process.on("SIGINT", () => {
    closeStandalone();
    process.exit(0);
  });
  process.on("SIGTERM", () => {
    closeStandalone();
    process.exit(0);
  });
}

await commands[command]();
