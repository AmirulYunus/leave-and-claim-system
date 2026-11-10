# Writing specs

Facts for the `ui-test` skill. The only origin is `http://127.0.0.1:3200`. The throwaway database is container `leave-e2e-db` on `127.0.0.1:55433`. `tests/e2e/support/constants.cjs` holds both values. `tests/e2e/support/global-setup.ts` refuses a non-loopback `baseURL` and refuses a sign-in that did not write a session to that container.

## Rules and recipes

| Topic                                                              | Read                                                                               |
| ------------------------------------------------------------------ | ---------------------------------------------------------------------------------- |
| Leave balances, categories, carry-forward, calendar                | `LEAVE_POLICY.md`, `docs/leave-rules/`                                             |
| Roles                                                              | `AGENTS.md`                                                                        |
| Product wording                                                    | `README.md`                                                                        |
| How a screen is reached, control names, success sentences, dialogs | `.cursor/skills/verify-leave-app/features/README.md` and the matching feature file |

Feature recipes that say port 3100 or `verify-only-password` describe a different check. Copy control names and gotchas only.

Area prefixes for scenario ids are `AUTH`, `PERM`, `LEAVE`, `CLAIM`, `TS`, and `EMP`. The kind is `POS`, `NEG`, `VAL`, or `PERM`. Number within the spec. Do not reuse an id.

## Accounts

Password for every seeded account is `e2e-only-password`. `npm run e2e:env` prints the same value.

| Fixture key         | Name         | Email                 | Role     |
| ------------------- | ------------ | --------------------- | -------- |
| `accounts.employee` | Amirul       | `amirul@example.test` | Employee |
| `accounts.hr`       | HR           | `hr@example.test`     | HR       |
| `accounts.admin`    | System Admin | `admin@example.test`  | Admin    |
| `accounts.sarah`    | Sarah Tan    | `sarah@example.test`  | Employee |
| `accounts.daniel`   | Daniel Lee   | `daniel@example.test` | Employee |

Seeded join dates are demo data. Do not assert Amirul's dashboard balance as the company rate.

## Fixture API

Import `test` and `expect` from `../support/fixtures` inside a spec. A spec that imports `test` from `@playwright/test` does not receive these fixtures.

| Fixture                               | What it gives you                                                                                                  |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `accounts`                            | The seeded people above, plus `accounts.password`.                                                                 |
| `db`                                  | `db.query(sql)` returns `string[][]`. Cells are strings. An empty result is `[]`. A count comes back as `[["3"]]`. |
| `uniqueTag`                           | Eight hex characters, different per test.                                                                          |
| `employeePage`, `hrPage`, `adminPage` | A page already signed in as that seeded person.                                                                    |
| `freshEmployee`                       | `{ id, name, email, password, signedInPage }`. `signedInPage()` returns a page signed in as the new employee.      |

`freshEmployee` is created through the HR API, then the temporary password is replaced. The saved name is `E2E <uniqueTag>`. The email is `e2e.<uniqueTag>@example.test`. The password is `New-<uniqueTag>-e2e-pw`. Join date is `2024-01-15`, level is 1, employment type is Full-time, and probation completed on `2024-04-15`. The fixture does not take a join date. Compute any balance from those facts and from `LEAVE_POLICY.md`.

Use `freshEmployee` when the scenario asserts a balance, a request list, or a timesheet. Use a seeded account for sign-in and for permission checks that do not depend on a balance. Put `uniqueTag` in the free-text field the screen saves, so a query can find the row.

`db.query` talks only to `leave-e2e-db`. Use it to read. Do not insert the leave, claim, or timesheet row the UI is supposed to create. During exploration, the same read is `node tests/e2e/support/environment.mjs sql "<query>"`.

`global-setup.ts` signs in as `hr@example.test` once before the suite. A count of every session, or of HR's sessions, changes even when the test did nothing. Count rows for the actor under test. Read the count before the action. After a negative, expect the same rows.

The suite sets `workers` to 1 because the database is shared. A test must still isolate its own rows.

## Helpers

Import from `../support/app`.

| Helper                          | Behavior                                                                                                                                                  |
| ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `signIn(page, email, password)` | Opens `/`, fills Email and Password, clicks Sign in. If the form does not change, clicks Sign in once more.                                               |
| `signOut(page)`                 | Clicks Sign out and expects the heading `Sign in to People & Co.`                                                                                         |
| `expectSuccess(page, message)`  | Expects a `status` that contains `message` and starts with `✓ `. Pass the sentence. The helper checks the checkmark.                                      |
| `expectError(page, message)`    | Expects an `alert` that contains `message`.                                                                                                               |
| `answerDialogs(page, answers)`  | Queue of strings. `null` dismisses. A string accepts a confirm, and is the typed answer for a prompt. Register it before the click that opens the dialog. |
| `malaysiaToday()`               | Today in `Asia/Kuala_Lumpur`, as `YYYY-MM-DD`.                                                                                                            |
| `addCalendarDays(iso, days)`    | Calendar offset. The result is `YYYY-MM-DD`.                                                                                                              |
| `nextWeekdayThisYear()`         | The next Monday to Friday that is still in the current calendar year.                                                                                     |

Do not hardcode a calendar date from a planning note. Read the date the dashboard shows, and build dates with these helpers. Leave that is not maternity, paternity, marriage, or compassionate has to fall in that year. The category file states the exceptions.

## Locators

Use `getByRole` and `getByLabel` with the accessible name from the latest snapshot.

- Dashboard headings include a space before the final period. The accessible name looks like `Good morning, HR .`. A regex such as `/Good morning, HR/` matches. An exact string without the space does not match.
- `Please wait…` and `Saving…` use the single ellipsis character `…`.
- Success text is on `role="status"`. Form errors and the failed-decision notice are on `role="alert"`. The failed-decision notice has a Retry button and no checkmark.
- The HR approvals control is a button whose name starts with `HR approvals` and includes the pending count.
- Fill one date field per action. Parallel fills drop the end date.
- During exploration, the first Sign in click is sometimes ignored. Click Sign in once more if the form is unchanged. Specs call `signIn`, which already does that. Do not add a sleep.
- Approval of some leave types opens `window.confirm` or `window.prompt`. The feature recipe names which ones. Specs use `answerDialogs`. Exploration uses `browser_handle_dialog` before the click.

## Scenario table

Show this shape to the tester.

| Id           | Type     | Actor        | Steps                               | Expected screen outcome                                                                               | Expected database effect        |
| ------------ | -------- | ------------ | ----------------------------------- | ----------------------------------------------------------------------------------------------------- | ------------------------------- |
| LEAVE-NEG-03 | negative | New employee | Submit Saturday through Sunday only | Alert `Choose at least one eligible date. Date ranges must be within one year.` The dialog stays open | No `requests` row for that note |

Types are `positive`, `negative`, `validation`, and `permission`.

## Spec shape

A Saturday through Sunday range has no weekday, so the client submits no dates. `validateLeave` in `app/lib/time.ts` returns the alert in this example.

```ts
import { test, expect } from "../support/fixtures";
import { expectError } from "../support/app";

test.describe("Leave approval", () => {
  test("LEAVE-NEG-03 weekend dates are refused", async ({
    freshEmployee,
    db,
  }) => {
    const note = "LEAVE-NEG-03 " + freshEmployee.email;
    const before = await db.query(
      `SELECT id FROM requests WHERE note = '${note}'`,
    );

    await test.step("Submit a Saturday through Sunday range", async () => {
      const page = await freshEmployee.signedInPage();
      // Put the note variable in Reason. Fill the two dates one action at a time, then submit.
      await expectError(
        page,
        "Choose at least one eligible date. Date ranges must be within one year.",
      );
    });

    expect(
      await db.query(`SELECT id FROM requests WHERE note = '${note}'`),
    ).toEqual(before);
  });
});
```

The example note is the scenario id plus the fixture email. Both come from the test, so that query's quotes are safe. Do not build SQL from tester-typed text.

## App bug

Keep every assertion that describes the correct behavior. Mark the test expected to fail.

```ts
test("LEAVE-NEG-03 weekend dates are refused", async ({ freshEmployee }) => {
  test.info().annotations.push({
    type: "app-bug",
    description:
      "A Saturday to Sunday range is saved. Weekend-only leave must be refused and must write no request.",
  });
  test.fail();
});
```

`test.fail()` with no argument expects a failure. If the test passes, the run fails, which means the bug may be fixed. Remove `test.fail()` and the annotation only after the rule and the screen agree.

## Evidence

| What                              | Where                                                       |
| --------------------------------- | ----------------------------------------------------------- |
| HTML report                       | `playwright-report/`, opened with `npm run test:e2e:report` |
| Spec video, screenshot, and trace | `test-results/`                                             |
| Machine-readable result           | `test-results/results.json`                                 |
| Exploration video and trace       | `.playwright-mcp/`                                          |

`playwright.config.ts` records video, screenshot, and trace for every test. Role pages from the fixtures attach a second screenshot and video on the test.

## Commands

| Command                                              | Effect                                                              |
| ---------------------------------------------------- | ------------------------------------------------------------------- |
| `npm run e2e:env`                                    | Starts the throwaway database and the app, and leaves them running. |
| `npm run e2e:stop`                                   | Stops that app and removes container `leave-e2e-db`.                |
| `npx playwright test tests/e2e/specs/<file>.spec.ts` | Runs one spec. Reuses the app when `e2e:env` is up.                 |
| `npm run test:e2e`                                   | Runs every spec.                                                    |
| `npm run test:e2e:headed`                            | Runs every spec with the browser visible.                           |
| `npm run test:e2e:ui`                                | Opens the Playwright runner.                                        |
| `npm run test:e2e:report`                            | Opens the last HTML report.                                         |

When `e2e:env` started the app, teardown leaves the container up. When Playwright started the app itself, teardown removes the container unless `E2E_KEEP` is `1`.
