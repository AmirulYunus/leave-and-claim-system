# Timesheet

An employee enters worked hours per date for a month and saves them. Approved leave appears in its own column and is never written into worked hours.

## Sub-features

- `timesheet-edit` keeps edits as a draft until saved.
- `timesheet-save` persists worked hours (0 to 24; a blank clears the date).
- `timesheet-leave` shows approved leave separately; worked hours plus approved leave cannot exceed 8 on a date.

## How to get to it (user POV)

- Sidebar `My timesheet`.

## Driving it with cursor-ide-browser

Preconditions:

- Signed in as `amirul@example.test`.

- **Pick month.** Click `My timesheet`. Heading `Monthly timesheet` appears. Fill textbox `Timesheet month` with `YYYY-MM`.
- **Enter hours.** Fill spinbutton `Worked hours YYYY-MM-DD` with `8`. Button `Save timesheet` becomes enabled.
- **Save.** Click `Save timesheet`. `✓ Timesheet saved to the database.` appears and the button reads `Timesheet saved`.
- **Proof.** `verify sql "SELECT t.date, t.hours FROM timesheet_entries t JOIN employees e ON e.id = t.employee_id WHERE e.email = 'amirul@example.test'"` returns the saved hours.

## Gotchas

- Unsaved edits block changing month (`Save the current month before changing months.`), submitting a request, reviewing requests, and `Sign out`.
- The help text says approved leave is added automatically; that applies to the displayed total only, not the stored worked hours.
- More than 8 worked hours with no leave is saved and flagged `Review extra hours`.
