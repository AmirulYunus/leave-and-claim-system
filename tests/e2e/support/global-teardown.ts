import { spawnSync } from "node:child_process";
import fs from "node:fs";
import { container, stateFile } from "./constants.cjs";

type RunState = { owner?: string };

export default async function globalTeardown() {
  if (process.env.E2E_KEEP === "1") return;
  if (!fs.existsSync(stateFile)) return;
  const state = JSON.parse(fs.readFileSync(stateFile, "utf8")) as RunState;
  // npm run e2e:env owns this container. Playwright reuses that server and must leave it up.
  if (state.owner !== "playwright") return;
  spawnSync("docker", ["rm", "-f", container], { encoding: "utf8" });
}
