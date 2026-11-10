#!/usr/bin/env node
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../../../..",
);
const runDir = path.join(root, ".verify", "run");
const evidenceRoot = path.join(root, ".verify", "evidence");
const stateFile = path.join(runDir, "state.json");
const logFile = path.join(runDir, "next.log");
const container = "leave-verify-db";
const dbPort = 55432;
const appPort = 3100;
const appUrl = `http://localhost:${appPort}`;
const seedPassword = "verify-only-password";
const windows = process.platform === "win32";
const pathKey =
  Object.keys(process.env).find((key) => key.toUpperCase() === "PATH") ??
  "PATH";
const env = {
  ...process.env,
  [pathKey]: [
    path.join(root, "node_modules", ".bin"),
    process.env[pathKey],
  ].join(path.delimiter),
  DATABASE_URL: `postgresql://leave_app:verify_only_db@127.0.0.1:${dbPort}/leave_and_claim`,
  SEED_PASSWORD: seedPassword,
  UPLOAD_DIR: path.join(runDir, "uploads"),
  EMAIL_DRIVER: "log",
  APP_URL: appUrl,
};

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const prismaBin = path.join(
  root,
  "node_modules",
  "prisma",
  "build",
  "index.js",
);
const nextBin = path.join(root, "node_modules", "next", "dist", "bin", "next");
const run = (command, args) =>
  spawnSync(command, args, { cwd: root, env, encoding: "utf8" });
const readState = () =>
  fs.existsSync(stateFile)
    ? JSON.parse(fs.readFileSync(stateFile, "utf8"))
    : null;

function fail(message) {
  console.error(`FAIL: ${message}`);
  process.exit(1);
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
    throw new Error(result.stderr.trim() || "psql failed");
  return result.stdout.trim();
}

function portFree(port) {
  return new Promise((resolve) => {
    const server = net.createServer();
    server.once("error", () => resolve(false));
    server.listen(port, "127.0.0.1", () => server.close(() => resolve(true)));
  });
}

function alive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function signIn(email) {
  const response = await fetch(`${appUrl}/api/auth/session`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password: seedPassword }),
  });
  return response.status;
}

function logTail() {
  return fs.existsSync(logFile)
    ? fs.readFileSync(logFile, "utf8").split("\n").slice(-25).join("\n")
    : "(no next.log)";
}

async function launch() {
  if (readState())
    fail("an instance is already recorded. Run doctor, or cleanup first.");
  if (run("docker", ["inspect", container]).status === 0)
    fail(
      `container ${container} exists from an earlier run. Run cleanup first.`,
    );
  if (run("docker", ["version"]).status !== 0)
    fail("Docker daemon is not running. Start Docker Desktop.");
  for (const port of [dbPort, appPort])
    if (!(await portFree(port)))
      fail(`port ${port} is in use by another process; refusing to share it.`);

  fs.mkdirSync(path.join(runDir, "uploads"), { recursive: true });
  const started = run("docker", [
    "run",
    "-d",
    "--rm",
    "--name",
    container,
    "-e",
    "POSTGRES_USER=leave_app",
    "-e",
    "POSTGRES_PASSWORD=verify_only_db",
    "-e",
    "POSTGRES_DB=leave_and_claim",
    "-p",
    `127.0.0.1:${dbPort}:5432`,
    "postgres:17",
  ]);
  if (started.status !== 0) fail(started.stderr.trim());
  fs.writeFileSync(stateFile, JSON.stringify({ container }, null, 2));

  let dbReady = false;
  for (let i = 0; i < 60 && !dbReady; i++) {
    try {
      dbReady = sql("SELECT 1") === "1";
    } catch {
      await sleep(1000);
    }
  }
  if (!dbReady) fail("database never accepted TCP queries. Run cleanup.");

  for (const args of [
    ["migrate", "deploy"],
    ["db", "seed"],
  ]) {
    const result = run(process.execPath, [prismaBin, ...args]);
    if (result.status !== 0)
      fail(`prisma ${args.join(" ")}\n${result.stdout}\n${result.stderr}`);
  }

  const log = fs.openSync(logFile, "a");
  const next = spawn(
    process.execPath,
    [nextBin, "dev", "--port", String(appPort), "--hostname", "127.0.0.1"],
    {
      cwd: root,
      env,
      detached: true,
      stdio: ["ignore", log, log],
      windowsHide: true,
    },
  );
  next.unref();
  fs.writeFileSync(
    stateFile,
    JSON.stringify({ container, pid: next.pid, appUrl }, null, 2),
  );

  for (let i = 0; i < 180; i++) {
    if (!alive(next.pid))
      fail(
        `next dev exited. If the log names a lock, another next dev runs in this checkout.\n${logTail()}`,
      );
    try {
      if ((await signIn("hr@example.test")) === 200) {
        console.log(`READY ${appUrl}`);
        console.log(
          `Accounts: hr@example.test (HR), amirul@example.test (Employee), admin@example.test (Admin), sarah@example.test, daniel@example.test`,
        );
        console.log(`Password: ${seedPassword}`);
        console.log(`Evidence: ${evidenceRoot}`);
        return;
      }
    } catch {}
    await sleep(1000);
  }
  fail(`app never accepted a sign-in within 180 s.\n${logTail()}`);
}

async function doctor() {
  const state = readState();
  const checks = [];
  const check = (ok, label) => checks.push([ok, label]);
  check(Boolean(state?.pid), "state recorded by launch");
  check(
    Boolean(state?.pid) && alive(state.pid),
    `next dev process ${state?.pid ?? "?"} alive`,
  );
  const running = run("docker", [
    "inspect",
    "-f",
    "{{.State.Running}}",
    container,
  ]);
  check(running.stdout.trim() === "true", `container ${container} running`);
  let before = null;
  try {
    const migrations = fs
      .readdirSync(path.join(root, "prisma", "migrations"))
      .filter((name) => /^\d/.test(name)).length;
    const applied = Number(
      sql(
        "SELECT count(*) FROM _prisma_migrations WHERE finished_at IS NOT NULL",
      ),
    );
    check(
      applied === migrations,
      `migrations applied ${applied}/${migrations}`,
    );
    check(
      Number(sql("SELECT count(*) FROM employees")) >= 5,
      "seed accounts present",
    );
    before = Number(sql("SELECT count(*) FROM sessions"));
  } catch (error) {
    check(false, `database query: ${error.message}`);
  }
  let status = 0;
  try {
    status = await signIn("hr@example.test");
  } catch {}
  check(status === 200, `sign-in at ${appUrl} (HTTP ${status})`);
  if (before !== null && status === 200)
    check(
      Number(sql("SELECT count(*) FROM sessions")) === before + 1,
      `app on ${appPort} writes to ${container}`,
    );
  for (const [ok, label] of checks)
    console.log(`${ok ? "OK  " : "FAIL"} ${label}`);
  if (checks.some(([ok]) => !ok)) process.exit(1);
}

function checksCommand() {
  if (!readState()?.pid) fail("no instance. Run launch first.");
  const scripts = fs
    .readdirSync(path.join(root, "scripts"))
    .filter((name) => /^check-.*\.mjs$/.test(name));
  let failed = 0;
  for (const script of scripts) {
    const result = run("node", [path.join("scripts", script)]);
    const output = `${result.stdout}${result.stderr}`.trim().split("\n").pop();
    console.log(
      `${result.status === 0 ? "PASS" : "FAIL"} ${script}: ${output}`,
    );
    if (result.status !== 0) failed++;
  }
  if (failed) process.exit(1);
}

function cleanup() {
  const state = readState();
  if (state?.pid && alive(state.pid)) {
    if (windows) run("taskkill", ["/PID", String(state.pid), "/T", "/F"]);
    else process.kill(-state.pid, "SIGTERM");
  }
  run("docker", ["rm", "-f", container]);
  fs.rmSync(runDir, { recursive: true, force: true });
  console.log(`Cleaned up. Evidence kept in ${evidenceRoot}`);
}

function evidence(name) {
  if (!name) fail("usage: evidence <name>");
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const dir = path.join(evidenceRoot, `${stamp}-${name}`);
  fs.mkdirSync(dir, { recursive: true });
  console.log(dir);
}

const [command, ...rest] = process.argv.slice(2);
const commands = {
  launch,
  doctor,
  checks: checksCommand,
  cleanup,
  evidence: () => evidence(rest[0]),
  sql: () => console.log(sql(rest.join(" "))),
};
if (!commands[command])
  fail(`usage: verify.mjs ${Object.keys(commands).join("|")}`);
await commands[command]();
