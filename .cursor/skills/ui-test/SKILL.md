---
name: ui-test
description: >-
  Explores the People & Co. leave and claims UI with Playwright on the isolated
  test app, turns business rules into positive, negative, validation, and
  permission scenarios, writes Playwright specs, runs them, and reports pass,
  fail, and expected-fail results with videos and traces. Use when the user
  says test a feature, test the leave approval feature, test the claims
  feature, check that employees cannot see HR approvals, write UI tests for a
  feature, run the UI tests, why did the UI test fail, or update UI tests
  after a screen change.
---

# UI tests

Testers ask in plain language. You explore the isolated app and write scenarios from the business rules. You save them as Playwright specs, run those specs, and report what passed, what failed, and which failures are known app bugs.

The only app URL is `http://127.0.0.1:3200`. Never point Playwright MCP or a spec at another origin. That includes the dev server on port 3000, the verify app on port 3100, `localhost`, and any deployed host.

Before writing a scenario or a spec, read [Writing specs](references/writing-specs.md). Conventions in `.cursor/rules/e2e-tests.mdc` are mandatory. Do not edit `app/` to make a test pass. Do not delete `tests/e2e/support/global-setup.ts` or remove `globalSetup` from `playwright.config.ts`.

## 1. Identify the feature

Name the feature from the tester's words. Read the matching UI recipe under `.cursor/skills/verify-leave-app/features/` and the rules that govern it.

- Leave balances, eligibility, dates, and carry-forward live in `LEAVE_POLICY.md` and `docs/leave-rules/`.
- Roles live in `AGENTS.md`. Employee, HR, and Admin are separate. There is no Superior role.
- Screen wording and entry points live in the feature recipe and `README.md`.

The feature recipes mention `http://localhost:3100` and the password `verify-only-password`. Ignore both. Use this skill's URL and the password `e2e-only-password`. Drive the screen with Playwright MCP. The verify skill's browser steps belong to the other check.

Demo balances are not company policy. Compute an expected balance from the rules and from the join date of the employee the test creates.

## 2. Start the isolated app

If `http://127.0.0.1:3200` is not already this test app, start `npm run e2e:env` in the background from the repo root.

Wait until the log prints `READY http://127.0.0.1:3200`. The earlier line `Test database leave-e2e-db is ready.` comes before the app is listening.

If the log says the test app is already running, use that instance. If it says port 3200 is in use and this test app is not the listener, stop and tell the tester. Do not kill an unknown process. Suggest `npm run e2e:stop` only when the stuck process is a previous test app.

The suite shares one database and one app. Do not start a second `npm run e2e:env` or a second full `npm run test:e2e` while one is already running.

Leave `e2e:env` running when you finish. Playwright reuses it. `npm run e2e:stop` only when the tester asks to stop.

## 3. Explore with Playwright

Use the `playwright` MCP server from `.cursor/mcp.json`. If those tools are not available, tell the tester to enable Playwright under Cursor Settings, then MCP. Do not switch to another browser tool and do not open another URL.

Before the first action, call `browser_start_video` and `browser_start_tracing`. After exploration, call `browser_stop_video` and `browser_stop_tracing`. Recordings land in `.playwright-mcp/`.

Navigate only to `http://127.0.0.1:3200`. Take `browser_snapshot` before each action and use the new refs. Fill date fields one action at a time. Call `browser_handle_dialog` before a click that opens a confirm or a prompt.

Drive the screen a tester would use. Do not insert leave, claims, or timesheet rows with SQL. SQL is for reading what the screen wrote.

## 4. Write the scenario table

Cover the feature with four kinds of scenario where the rules allow them. The kinds are positive, negative, validation, and permission.

Each row has an id, a type, an actor, the steps, the expected screen outcome, and the expected database effect. Ids look like `LEAVE-NEG-03`. The middle part is `POS`, `NEG`, `VAL`, or `PERM`. Reuse the area prefix already in that spec. For a new area use `AUTH`, `PERM`, `LEAVE`, `CLAIM`, `TS`, or `EMP`.

A negative row states the screen text and states that no row was written. Derive the expected text and the expected row from the rules and from what exploration showed. Do not pick an expected result because it matches what the app does today.

If the tester asked for a plan, show the table and wait. Otherwise keep the table and continue.

## 5. Write the spec

Add or extend one spec under `tests/e2e/specs/`. One `test.describe` per feature area. Import `test` and `expect` from `tests/e2e/support/fixtures.ts`. Import helpers from `tests/e2e/support/app.ts`.

The title starts with the scenario id, for example `LEAVE-NEG-03 weekend dates are refused`. Each user-visible step is a `test.step`. Create isolated rows with `freshEmployee` or `uniqueTag`. Assert the visible outcome and the database row. For a negative, assert that nothing was written for that actor.

Use `getByRole` and `getByLabel`. Do not call `page.waitForTimeout`.

A confirmed app bug stays as a test of the correct behavior, marked `test.fail()`, with annotation `{ type: "app-bug", description }`. Never weaken an assertion to make it pass.

## 6. Run and classify failures

Run the file you edited.

```bash
npx playwright test tests/e2e/specs/<file>.spec.ts
```

Run `npm run test:e2e` only when the tester asked to run everything.

On failure, classify before editing.

- A test or setup defect is a wrong locator, a misread rule, or a shared row. A session count that includes the safety check's HR sign-in is the same kind of defect. Fix the spec or the helper.
- An app bug is a screen or a database row that disagrees with `LEAVE_POLICY.md`, `docs/leave-rules/`, `AGENTS.md`, or `README.md`. Confirm it by reading that rule and by seeing the running app. Keep the correct-behavior assertions. Add `test.fail()` and the `app-bug` annotation. Do not edit `app/`.

If a `test.fail()` test passes, the bug may be gone. Re-read the rule. When the app now matches the rule, remove `test.fail()` and the annotation.

## 7. Report to the tester

Write the report in plain language.

```markdown
## Result

N passed. N failed. N expected fails are app bugs.

## What was tested

One line per scenario, in the tester's words.

## App bugs

- `LEAVE-NEG-03`. What the screen did. What the rule says should happen.

## Where to look

- HTML report. Run `npm run test:e2e:report`.
- Videos, screenshots, and traces from the run are in `test-results/`.
- Exploration video and trace are in `.playwright-mcp/`.

## Run this again

`npx playwright test tests/e2e/specs/<file>.spec.ts`
```

Say when there are no app bugs. Do not tell the tester to point the run at another URL.

## Maintain tests

When UI text changes, confirm the new wording was intended. Then update the locator or the expected text. If the new wording contradicts a rule, keep the old expectation and mark an app bug as in step 6.

When a control moves but the accessible name stays, prefer the role and the name over a new CSS selector.
