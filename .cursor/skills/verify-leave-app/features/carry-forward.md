# Annual Leave carry-forward

HR and Admin see each active employee's Annual Leave carry-forward for the closing year on the Employees page. From 1 October to 15 December HR can record a written approval for a higher limit; from 1 January to 31 March HR confirms each carry-forward.

## Sub-features

- `cf-preview` shows Unused, Limit, Carry forward and Status per employee.
- `cf-exception` records a written higher limit (`Record written approval`), 1 October to 15 December.
- `cf-confirm` confirms the carry-forward (`Confirm`), 1 January to 31 March.

## How to get to it (user POV)

- As HR or Admin, sidebar `Employees`, heading `Annual Leave carry-forward from <year>`.

## Driving it with cursor-ide-browser

Preconditions:

- Signed in as `hr@example.test`. Today (Malaysia time) falls in a window: 1 October to 15 December for `cf-exception`, 1 January to 31 March for `cf-confirm`.

- **Preview.** Click `Employees`. The heading names the closing year (the current year from April to December, last year from January to March). Columns `Employee`, `Unused`, `Limit`, `Carry forward`, `Status`, `Action`. Status reads `Not confirmed`; Limit defaults to `5 days`.
- **Record written approval.** Stub `window.prompt` with `['6', 'CF-2026-AMIRUL']` (prompts `Written approval for <name>: the higher carry-forward limit in days (more than 5, in half days).` and `Reference of the written approval document:`), then click `Record written approval` on Amirul's row. Expect `Written carry-forward exception recorded.`, Limit `6 days` and `Written approval: CF-2026-AMIRUL`.
- **Proof.** `verify sql "SELECT employee_id, source_year, limit_hours, document_reference FROM carry_forward_exceptions"` returns `source_year` of the closing year and `limit_hours` 48.

## Gotchas

- **Fails today (product bug, 10 November 2026).** The page calls `GET /api/carry-forward` with no `year`. `app/api/carry-forward/route.ts:62` turns the missing parameter into year 0, so the heading reads `from 0`, every row shows `0 days`, and neither action button appears. Report this journey as failed until the route is fixed; do not pass `?year=` by hand to make it pass.
- `scripts/check-carry-forward.mjs` passes regardless, because it reuses the returned year and skips the exception path when `canRecordException` is false.
- Prompts are in days; the API stores hours. A limit must be above 5 days, in half days, at most 1,000 hours. One exception per employee per year.
