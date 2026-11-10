# Employees

HR and Admin add and edit employee profiles, including the employment details that drive entitlements, and maintain department and cost centre lists. HR can create Employee and HR accounts and edit Employee rows; only Admin creates Admin accounts or changes roles.

## Sub-features

- `employee-add` creates a profile with a temporary password.
- `employee-edit` updates employment details (position, type, level, probation, leaving date, department, cost centre, active).
- `employee-role` lets Admin change a role.
- `org-lists` adds, deactivates and reactivates departments and cost centres.

## How to get to it (user POV)

- As HR or Admin, sidebar `Employees` (heading `Your people, in one place.`), then button `Add employee` or a row's `Edit`.

## Driving it with cursor-ide-browser

Preconditions:

- Signed in as `hr@example.test`.

- **Add.** Click `Employees`, then `Add employee`. Dialog `Add employee` opens. Fill `Name`, `Email` (for example `verify-leave-app@example.test`), `Join date`, `Temporary password` (12 or more characters). Choose `Employment type` (`Full-time`, `Part-time`, `Intern`) and `Employee level` (`1. Operations / Analyst / Senior Analyst`, `2. Consultant / Senior Consultant`, `3. Sales / Lead Consultant (Manager)`, `4. Principal Consultant (Senior Manager) / Director`). Click `Save employee`. `✓ Employee profile saved.` appears and the row shows `Temporary password`.
- **Edit.** Click the row's `Edit`. Dialog `Edit employee` has `Email` disabled, `Role` limited to `Employee` for HR, an `Active employee` checkbox and a `Reset temporary password` field. Change `Employee level` and click `Save employee`; the row shows the new level.
- **Lists.** Under `Departments`, fill textbox `New department` and click its `Add`. `✓ Organisation list saved.` appears. Cost centres use textbox `New cost centre`.
- **Proof.** `verify sql "SELECT email, role, employment_type, employee_level, must_change_password FROM employees WHERE email = 'verify-leave-app@example.test'"` returns the saved values and `t` until the person changes their password. `verify sql "SELECT name, is_active FROM departments"` lists the new department.

## Gotchas

- A blank employment type or level leaves entitlements pending HR details rather than zero.
- The disposable database is discarded by cleanup, so no profile cleanup is needed.
