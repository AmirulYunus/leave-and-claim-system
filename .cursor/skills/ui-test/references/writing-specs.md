# Writing specs

Facts for the `ui-test` skill. The only origin is `http://127.0.0.1:3200`. The throwaway database is container `leave-e2e-db` on `127.0.0.1:55433`. `tests/e2e/support/constants.cjs` holds both values. `tests/e2e/support/global-setup.ts` allows only `127.0.0.1`, refuses a second worker, refuses a program on port 3200 that is not the test app, and refuses a sign-in that did not write a session to that container.

## Rules and recipes

| Topic                                                              | Read                                                                               |
| ------------------------------------------------------------------ | ---------------------------------------------------------------------------------- |
| Leave balances, categories, carry-forward, calendar                | `LEAVE_POLICY.md`, `docs/leave-rules/`                                             |
| Roles                                                              | `AGENTS.md`                                                                        |
| Product wording                                                    | `README.md`                                                                        |
| How a screen is reached, control names, success sentences, dialogs | `.cursor/skills/verify-leave-app/features/README.md` and the matching feature file |

Feature recipes that say port 3100 or `verify-only-password` describe a different check. Copy control names and gotchas only.

Area prefixes for scenario ids are `AUTH`, `PERM`, `LEAVE`, `CLAIM`, `TS`, and `EMP`. The kind is `POS`, `NEG`, `VAL`, or `PERM`. Ids are unique across `tests/e2e/specs`. A new file in an existing area continues that area's numbering. Cancellation uses `LEAVE-*`.

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

`db.query` talks only to `leave-e2e-db`. Use it to read. Do not insert the leave, claim, or timesheet row the UI is supposed to create. During exploration, the same read is `node tests/e2e/support/environment.mjs sql "<query>"`. The verify skill's `verify sql` belongs to the other harness.

`global-setup.ts` signs in as `hr@example.test` once before the suite. A count of every session, or of HR's sessions, changes even when the test did nothing. Count rows for the actor under test. Read the count before the action. After a negative, expect the same rows.

The suite sets `workers` to 1 because the database is shared. A test must still isolate its own rows.

## Support files

Import `test` and `expect` from `../support/fixtures`.

- `ui.ts` holds page flows (`submitLeave`, `decide`, `openApprovals`, `submitClaim`, `openTimesheet`).
- `app.ts` holds sign-in, assertions, and dates (`signIn`, `expectSuccess`, `expectError`, `answerDialogs`, `malaysiaToday`).
- `records.ts` holds database readers (`leaveRequest`, `claimRequest`, `sqlString`).
- `rules.ts` holds policy math (`yearPlan`, `futureWeekday`, `leaveSummary`).
- `provision.ts` creates employees (`createTemporaryEmployee`).
- `fixtures.ts` provides `accounts`, `db`, `uniqueTag`, and the signed-in pages.
- `constants.cjs` holds the port, the origin, and the throwaway database URL.
- `environment.mjs` starts the throwaway app, stops it, and runs one SQL query.
- `global-setup.ts` refuses a foreign server, a second worker, or the wrong database.

| Helper                                    | Behavior                                                                                                                                                                                                   |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `signIn(page, email, password)`           | Opens `/`, fills Email and Password, and clicks Sign in once.                                                                                                                                              |
| `signOut(page)`                           | Clicks Sign out and expects the heading `Sign in to People & Co.`                                                                                                                                          |
| `expectSuccess(page, message)`            | Expects a `status` that contains `message` and starts with `✓ `. Pass the sentence. The helper checks the checkmark.                                                                                       |
| `expectError(page, message)`              | Expects an `alert` that contains `message`.                                                                                                                                                                |
| `answerDialogs(page, answers, expected?)` | Queue of strings. `null` dismisses. A string accepts a confirm, and is the typed answer for a prompt. An optional third argument asserts `dialog.message()` for each dialog. Register it before the click. |
| `malaysiaToday()`                         | Today in `Asia/Kuala_Lumpur`, as `YYYY-MM-DD`.                                                                                                                                                             |
| `addCalendarDays(iso, days)`              | Calendar offset. The result is `YYYY-MM-DD`.                                                                                                                                                               |
| `yearPlan().weekdays[0]`                  | The first weekday on or after today, from 1 April of this year. On a weekday, that date is today.                                                                                                          |
| `futureWeekday(plan)`                     | The first weekday strictly after today in that plan, or undefined when this year has none left. Use it when the case needs a future date, including cancellation.                                          |

Do not hardcode a calendar date from a planning note. Read the date the dashboard shows, and build dates with these helpers. Leave that is not maternity, paternity, marriage, or compassionate has to fall in that year. The category file states the exceptions.

## Locators

Use `getByRole` and `getByLabel` with the accessible name from the latest snapshot.

- Dashboard headings include a space before the final period. The accessible name looks like `Good morning, HR .`. A regex such as `/Good morning, HR/` matches. An exact string without the space does not match.
- `Please wait…` and `Saving…` use the single ellipsis character `…`.
- Success text is on `role="status"`. Form errors and the failed-decision notice are on `role="alert"`. The failed-decision notice has a Retry button and no checkmark. Filter every `getByRole("alert")` on the sentence. Next devtools renders an empty alert in dev.
- The HR approvals control is a button whose name starts with `HR approvals` and includes the pending count. The Pending chip filters by request status only. A pending cancellation sits on an Approved request and is decided from the All view. `decide()` approves or rejects the request. It does not decide a cancellation.
- When the refusal hides the control, assert that the control is missing and that the row on screen is unchanged. Then check the server refusal with `page.request` on the signed-in page. `LEAVE-PERM-01` is the pattern.
- Fill one date field per action. Parallel fills drop the end date.
- Approval of some leave types opens `window.confirm` or `window.prompt`. The feature recipe names which ones. Specs use `answerDialogs`. Exploration with Playwright MCP uses `browser_handle_dialog` before the click.

## Scenario table

Show this shape to the tester.

| Id           | Type     | Actor        | Steps                               | Expected screen outcome                                                                               | Expected database effect        |
| ------------ | -------- | ------------ | ----------------------------------- | ----------------------------------------------------------------------------------------------------- | ------------------------------- |
| LEAVE-NEG-03 | negative | New employee | Submit Saturday through Sunday only | Alert `Choose at least one eligible date. Date ranges must be within one year.` The dialog stays open | No `requests` row for that note |

Types are `positive`, `negative`, `validation`, and `permission`.

## Spec shape

Copy this shape. It is the passing one-weekday leave test. `submitLeave` fills the form. The status banner and `leaveRequest` are the observable result. Wrap any value you put into SQL with `sqlString`.

```ts
import { test, expect } from "../support/fixtures";
import { expectSuccess } from "../support/app";
import { leaveRequest, sqlString } from "../support/records";
import { leaveSummary, yearPlan } from "../support/rules";
import { requestRow, submitLeave } from "../support/ui";

test.describe("Leave requests", () => {
  test("LEAVE-POS-01 a full weekday stays pending", async ({
    freshEmployee,
    db,
    uniqueTag,
  }) => {
    const date = yearPlan().weekdays[0];
    const note = `LEAVE-POS-01 ${uniqueTag}`;
    const page = await freshEmployee.signedInPage();

    await test.step("Submit one full weekday", async () => {
      await submitLeave(page, {
        start: date,
        end: date,
        note,
        summary: leaveSummary(1, 8),
      });
      await expectSuccess(page, "Leave request saved for HR review.");
      await expect(requestRow(page, note)).toContainText("Pending");
    });

    expect(await leaveRequest(db, note)).toMatchObject({
      status: "Pending",
      reviewerId: "",
      email: freshEmployee.email,
    });
    expect(
      await db.query(
        `SELECT count(*)::text FROM requests WHERE note = ${sqlString(note)}`,
      ),
    ).toEqual([["1"]]);
  });
});
```

`LEAVE-POS-01` is already in `leave.spec.ts`. Take the next free id. Do not build SQL by putting a raw value between quotes.

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

`test.fail()` marks the whole test. A check that must pass, such as a row that must not be stored, belongs in its own test. The expected-fail test asserts only the presentation that is still wrong. If the expected-fail test passes, the bug may be fixed. Remove `test.fail()` and the annotation only after the rule and the screen agree.

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
