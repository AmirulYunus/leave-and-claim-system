#!/usr/bin/env node
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const skillDir = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);
const root = path.resolve(skillDir, "../../..");
const outDir = path.join(root, ".verify", "formal");
const jar = path.join(root, ".verify", "tools", "tla2tools.jar");
const jarUrl =
  "https://github.com/tlaplus/tlaplus/releases/download/v1.8.0/tla2tools.jar";
const jarSha256 =
  "E1F11B75C4B5BC4478BA0AC5CB6AE28E40A01B0016BC33E0AA97DA22DFDBCD7F";
const image = "eclipse-temurin:21-jre";

// Each knob: the handler, the re-read its checks depend on, and whether an
// employee-keyed advisory lock comes before that re-read.
const handlers = [
  [
    "SubmitLocks",
    "app/api/workspace/route.ts",
    "POST",
    "validateLeaveEntries(",
  ],
  [
    "DecideLocks",
    "app/api/workspace/route.ts",
    "PATCH",
    "const target = await tx.requests.findUnique(",
  ],
  [
    "CancelLocks",
    "app/api/cancellations/route.ts",
    "POST",
    "const leave = await tx.requests.findUniqueOrThrow(",
  ],
  [
    "DecideCancelLocks",
    "app/api/cancellations/route.ts",
    "PATCH",
    "const cancellation = await tx.cancellations.findUniqueOrThrow(",
  ],
];

function fail(message) {
  console.error(`FAIL: ${message}`);
  process.exit(2);
}

function handlerBody(source, method) {
  const start = source.indexOf(`export async function ${method}(`);
  if (start < 0) return null;
  const next = source.indexOf("\nexport ", start + 1);
  return {
    text: source.slice(start, next < 0 ? undefined : next),
    offset: start,
  };
}

function extract() {
  return handlers.map(([knob, file, method, anchor]) => {
    const source = fs.readFileSync(path.join(root, file), "utf8");
    const body = handlerBody(source, method);
    if (!body)
      fail(
        `${file} has no ${method} handler. The model is out of sync with the code.`,
      );
    const anchorAt = body.text.indexOf(anchor);
    if (anchorAt < 0)
      fail(
        `${file} ${method} no longer contains "${anchor}". Update the model and this anchor.`,
      );
    const lock = /pg_advisory_xact_lock\(\$\{[^}]*employee[^}]*\}/.exec(
      body.text,
    );
    const locks = Boolean(lock) && lock.index < anchorAt;
    const line = (at) => source.slice(0, body.offset + at).split("\n").length;
    const where = lock
      ? `lock at line ${line(lock.index)}, re-read at line ${line(anchorAt)}`
      : `no employee lock, re-read at line ${line(anchorAt)}`;
    return { knob, locks, where: `${file} ${method}: ${where}` };
  });
}

function ensureJar() {
  if (!fs.existsSync(jar)) {
    fs.mkdirSync(path.dirname(jar), { recursive: true });
    const download = spawnSync("curl", ["-fsSL", "-o", jar, jarUrl], {
      stdio: "inherit",
    });
    if (download.status !== 0) fail(`could not download ${jarUrl}`);
  }
  const sha = createHash("sha256")
    .update(fs.readFileSync(jar))
    .digest("hex")
    .toUpperCase();
  if (sha !== jarSha256)
    fail(
      `tla2tools.jar SHA-256 ${sha} does not match the pinned ${jarSha256}.`,
    );
}

const forced = Object.fromEntries(
  process.argv
    .slice(2)
    .filter((arg) => arg.startsWith("--force="))
    .map((arg) => arg.slice(8).split("=")),
);
const knobs = extract().map((k) =>
  k.knob in forced
    ? {
        ...k,
        locks: forced[k.knob] === "TRUE",
        where: `${k.where} (forced ${forced[k.knob]})`,
      }
    : k,
);
for (const k of knobs)
  console.log(`${k.knob} = ${k.locks ? "TRUE " : "FALSE"}  ${k.where}`);

if (spawnSync("docker", ["version"]).status !== 0)
  fail("Docker daemon is not running. Start Docker Desktop.");
ensureJar();
fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(
  path.join(outDir, "LeaveLocks.cfg"),
  [
    "SPECIFICATION Spec",
    ...knobs.map((k) => `CONSTANT ${k.knob} = ${k.locks ? "TRUE" : "FALSE"}`),
    "CONSTANT Allowance = 1",
    "INVARIANTS NoOverspend DecidedOnce OnePendingCancellation CancelledOnce",
    "PROPERTY LegalTransitions",
    "",
  ].join("\n"),
);

const tlc = spawnSync(
  "docker",
  [
    "run",
    "--rm",
    "-v",
    `${root}:/work`,
    "-w",
    "/work/.cursor/skills/verify-leave-concurrency/model",
    image,
    "java",
    "-XX:+UseParallelGC",
    "-cp",
    "/work/.verify/tools/tla2tools.jar",
    "tlc2.TLC",
    "-config",
    "/work/.verify/formal/LeaveLocks.cfg",
    "-deadlock",
    "-noGenerateSpecTE",
    "-workers",
    "auto",
    "-metadir",
    "/tmp/tlc",
    "LeaveLocks.tla",
  ],
  { encoding: "utf8" },
);
const output = `${tlc.stdout}${tlc.stderr}`;
fs.writeFileSync(path.join(outDir, "last-run.txt"), output);
const violated =
  /(Invariant \w+ is violated|Temporal properties were violated|Action property \w+ .*violated)/.exec(
    output,
  );
if (violated) {
  console.log(`\nVIOLATION: ${violated[0]}\n`);
  const trace = output.slice(
    output.indexOf("Error: The behavior up to this point"),
  );
  console.log(trace.split("\n").slice(0, 120).join("\n"));
  console.log(`\nFull output: ${path.join(outDir, "last-run.txt")}`);
  process.exit(1);
}
if (!/Model checking completed\. No error has been found\./.test(output)) {
  console.log(output.split("\n").slice(-30).join("\n"));
  fail("TLC did not finish cleanly. See .verify/formal/last-run.txt.");
}
const states =
  /^\d+ states generated, (\d+) distinct states found, 0 states left on queue/m.exec(
    output,
  )?.[1];
console.log(
  `\nPASS: all invariants and LegalTransitions hold (${states} distinct states).`,
);
