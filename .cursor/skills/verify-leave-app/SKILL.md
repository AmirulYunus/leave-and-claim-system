---
name: verify-leave-app
description: Launch an isolated instance of the People & Co. leave and claims Next.js app on a throwaway Postgres, drive it in the browser as Employee/HR/Admin, and capture proof (screenshots plus database rows). Use to prove a leave, claim, timesheet, employee or sign-in change works end to end, or when asked to verify the leave app.
disable-model-invocation: true
---

# Verify the leave app

The surface is the web UI at `http://localhost:3100`, served by `next dev` against a disposable Postgres container. The JSON API under `/api/*` is secondary and is covered by `scripts/check-*.mjs`.

All commands run from the repo root. The helper is `node .cursor/skills/verify-leave-app/scripts/verify.mjs <command>`; below it is `verify`.

## Launch

1. Docker Desktop must be running (`docker version` shows a Server).
2. `verify launch` starts container `leave-verify-db` (postgres:17, no volume, `127.0.0.1:55432`), applies Prisma migrations, runs the seed, then starts `next dev` on port 3100. It prints `READY http://localhost:3100` only after HR can sign in. A cold start takes about 20 to 30 seconds.
3. Seeded accounts, all with password `verify-only-password`: `amirul@example.test` (Employee, level 1, joined 2024-01-15), `hr@example.test` (HR), `admin@example.test` (Admin), `sarah@example.test`, `daniel@example.test` (Employees).

Launch refuses to start when its state, container or ports already exist. Run `verify doctor`; if the instance is broken, run `verify cleanup` and launch again.

Only one `next dev` may run per checkout (Next 16 lockfile). If launch fails with a lock message, the user's own dev server is running. Stop and tell the user. Never kill their server.

## Doctor

`verify doctor` is the "is this instance worth driving?" check. It confirms the recorded `next dev` process is alive, the container runs, all migrations are applied, the seed accounts exist, sign-in returns 200, and the app on 3100 writes sessions to `leave-verify-db`, not to the user's database. Run it first whenever anything looks off.

## Drive

Use the `cursor-ide-browser` MCP tools. Before acting, read the [feature map](features/README.md) and use the matching feature file as the recipe.

- `browser_navigate` to `http://localhost:3100/` with `newTab: true`, then `browser_lock`. The navigate snapshot stamps `data-cursor-ref` attributes onto the server HTML before React hydrates, which Next reports as a hydration error at `app/auth-screen.tsx`. Clear it before driving: `browser_cdp` `Runtime.evaluate` `location.reload()`, then wait about 5 seconds (an awaited `setTimeout`) before the next snapshot. The reloaded page shows no issues badge.
- Take a `browser_snapshot` (with `interactive: true`) before each action. Refs change after every render, so target elements by role and accessible name taken from the latest snapshot.
- Fill one field per call and wait for its result. Parallel fills on date inputs drop values.
- Native `window.prompt` and `window.confirm` dialogs block the page. Before the click that opens one, stub it with `browser_cdp` `Runtime.evaluate`: `window.__answers = ['<answer>', ...]; window.prompt = () => window.__answers.shift() ?? null; window.confirm = () => true;`. Sign-in and sign-out reload the page, so stub again after them. Record the stubbed answers in the evidence; the stub replaces only the typing into the dialog.
- File inputs cannot be set with `DOM.setFileInputFiles` (denied). Assign a `DataTransfer` file to the input with `Runtime.evaluate` and dispatch a bubbling `change` event; [Supporting documents](features/supporting-documents.md) has the snippet.
- `browser_lock` with `unlock` when done.

API regression: `verify checks` runs every `scripts/check-*.mjs` against the instance. Each script deletes the data and outbox rows it creates.

## Evidence

- `verify evidence <name>` creates and prints `.verify/evidence/<timestamp>-<name>/`. Put every artifact for the run there.
- `browser_take_screenshot` saves to `%TEMP%\cursor\screenshots\<filename>` (on Windows). Copy each screenshot into the evidence folder.
- `verify sql "<query>"` runs a query against `leave-verify-db` and prints tab-separated rows. Save the side-effect proof (request rows, `reviewed_by`, `leave_entries`, `notifications`) to a text file in the evidence folder.

Proof standards:

- Drive the real user path in the browser. Do not call `/api/*` directly to produce the state you claim the UI produced.
- Capture the action and the resulting state: a screenshot after each step, plus the database row it wrote.
- Verify side effects as well as the screen. A decision writes `requests.status`, `requests.reviewed_by`, `requests.reviewed_at`, and one `notifications` row per recipient.
- Emails are never sent. `EMAIL_DRIVER=log` keeps them in the `notifications` outbox, which is the observable.

## Cleanup

`verify cleanup` kills the recorded `next dev` process tree, removes container `leave-verify-db` and deletes `.verify/run/` (state, log, uploads). It keeps `.verify/evidence/`. It never touches the user's `leave-and-claim-system-db-1` container, its volume, or port 3000. Run it after every failed attempt as well as at the end.

## Files

- `scripts/verify.mjs` is the helper (`launch | doctor | checks | sql | evidence | cleanup`).
- `features/` is the feature map. Keep it in step with the app via `/maintain-verification-skill`.
- `.verify/run/next.log` is the dev server log while an instance runs.
