# Leave app verification map

This directory is the maintained source for verifying the user-facing behavior of the People & Co. leave and claims app. Read this index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- `verify launch` reported `READY http://localhost:3100` and `verify doctor` passes.
- The database is fresh from launch: seed accounts only, no requests.
- Today's date matters. Annual Leave can only use leave earned in completed months of the current calendar year, and requests must fall in the current year.

## Driving conventions

- Start each recipe signed out. Sign in with the account the recipe names (see [Sign in](./sign-in.md)).
- Target elements by role and accessible name from a fresh `browser_snapshot`. Never reuse a ref across renders.
- One field per `browser_fill` call.
- Mark free-text fields with `verify-leave-app` so proof queries can find the rows (`WHERE note = '...'`).
- Success banners start with `✓ `. Native dialogs and file uploads follow the techniques in the skill's Drive section.

## Proof and skip reporting

- A screenshot after each user step, copied into the run's evidence folder.
- A `verify sql` readback of every row the step should have written.
- Report an unreachable path with the attempted step and the unmet precondition. Do not report a skipped entry point as verified through another path.

## Feature entry contract

Each feature file has an H1, one paragraph of user-visible behavior, then exactly four H2 sections: `Sub-features`, `How to get to it (user POV)`, `Driving it with cursor-ide-browser`, `Gotchas`.

## Features

Last full live pass: 10 November 2026. Every feature below was driven in the browser; carry-forward failed on a product bug recorded in its Gotchas.

- [Sign in](./sign-in.md) covers sign-in, sign-out and the forced temporary-password change.
- [Leave request and approval](./leave-request-approval.md) covers submitting leave and HR approving or rejecting it.
- [Leave cancellation](./leave-cancellation.md) covers cancelling approved future leave and HR's decision.
- [Employees](./employees.md) covers HR adding and editing profiles, employment details, departments and cost centres.
- [Timesheet](./timesheet.md) covers entering and saving worked hours.
- [Expense claims](./expense-claims.md) covers submitting a claim and HR's decision.
- [Annual Leave carry-forward](./carry-forward.md) covers HR's preview, written higher limits and confirmation.
- [Supporting documents](./supporting-documents.md) covers attaching and downloading leave documents.
