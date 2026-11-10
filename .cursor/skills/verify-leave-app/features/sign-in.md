# Sign in

A user signs in with email and password, signs out, and an account with a temporary password must choose a new one before using the app.

## Sub-features

- `signin-ok` signs in with a valid password and lands on the dashboard.
- `signin-bad` shows an error for a wrong password.
- `signout` returns to the sign-in form.
- `temp-password` forces a password change for an account HR created.

## How to get to it (user POV)

- Open `http://localhost:3100/` while signed out.
- Choose `Sign out` in the top bar.

## Driving it with cursor-ide-browser

Preconditions:

- Signed out. The page shows heading `Sign in to People & Co.` and `Use the email address and password from HR.`

- **Sign in.** Fill textbox `Email`, then textbox `Password` (`verify-only-password`), then click button `Sign in`. The button reads `Please wait…` and the page reloads onto the dashboard heading `Good morning, <name> .` (accessible name has a space before the period) with buttons `Dashboard`, `My leave`, `My claims`, `My timesheet`, plus `HR approvals <pending count>` and `Employees` for HR and Admin.
- **Wrong password.** Same steps with any other password. Alert `Incorrect email or password.` appears and the form stays. No `sessions` row is written.
- **Sign out.** Click button `Sign out`. The sign-in form returns.
- **Temporary password.** Create an employee with a temporary password (see [Employees](./employees.md)), sign out, and sign in as that employee. Heading `Choose a new password` appears with textboxes `Temporary password`, `New password`, `Confirm new password`, hint `Use 12 to 128 characters.`, and buttons `Sign out` and `Save password`. Saving lands on `Good morning, <name> .`
- **Proof.** `verify sql "SELECT e.email, count(*) FROM sessions s JOIN employees e ON e.id = s.employee_id GROUP BY e.email"` shows a session for the account; `employees.must_change_password` is `f` after a temporary-password change.

## Gotchas

- A hydration error at `app/auth-screen.tsx` (67:11) means the navigate snapshot ran before hydration. It is a harness artifact, not an app bug; reload as the skill's Drive section says.
- The first `Sign in` click after filling the form is sometimes swallowed, even on a cleanly hydrated page: the button never shows `Please wait…` and `.verify/run/next.log` has no `POST /api/auth/session`. Cause unknown. Click again once.
- The 12 to 128 character rule applies to new passwords only. The sign-in field just caps length at 128.
- `Sign out` is disabled while a timesheet has unsaved edits.
