# Leave request and approval

An employee submits a leave request from one form. HR sees it under HR approvals and approves or rejects it. The decision is saved with the reviewer and emails the employee and every active HR account.

## Sub-features

- `leave-submit` creates a Pending request with one `leave_entries` row per weekday.
- `leave-approve` sets Approved, records the HR reviewer and queues the decision emails.
- `leave-reject` sets Rejected, records the reviewer and queues the rejection emails.
- `leave-self` blocks HR from deciding their own request (`Another reviewer decides`).

## How to get to it (user POV)

- Dashboard buttons `Apply for leave` or `Plan your leave`, or sidebar button `Plan your next break ↗`.
- As HR or Admin, sidebar button `HR approvals <pending count>`.

## Driving it with cursor-ide-browser

Preconditions:

- Fresh instance. `amirul@example.test` has earned Annual leave this year. On 10 November 2026 the dashboard showed `12.5 days available` (level 1 base 14 days plus 1 service day, 10 completed months).
- Pick weekdays later this year that have no leave, for example `2026-12-02` and `2026-12-03`.

- **Open the form.** Sign in as `amirul@example.test`. Click `Apply for leave`. Dialog `Make time for yourself.` opens with combobox `Category` set to `Annual leave`.
- **Fill it.** Fill `Start date`, then `End date`, then `Reason` (for example `verify-leave-app approve`). Combobox `Duration for every date` offers `Full day (8 hours)` and `Half day (4 hours)`. A full day shows `1 weekday × 8 hours = 8 hours ( 1 days).`
- **Submit.** Click `Submit for approval`. The button reads `Saving…`, then `✓ Leave request saved for HR review.` appears and Recent activity lists the request as `Pending`.
- **Approve.** Click `Sign out`, sign in as `hr@example.test`, click `HR approvals <n>`. Each pending row has `Approve` and `Reject`. Click `Approve`: `✓ Request approved and saved.`, status `Approved`, decision `Reviewed`. Amirul's Annual leave card drops by the approved days.
- **Reject.** On a second pending request click `Reject`: `✓ Request rejected and saved.`, status `Rejected`. No dialog opens.
- **Proof.** `verify sql "SELECT r.note, r.status, rv.email, r.reviewed_at IS NOT NULL FROM requests r JOIN employees rv ON rv.id = r.reviewed_by WHERE r.note LIKE 'verify-leave-app%'"` lists each decision with `hr@example.test`. `verify sql "SELECT recipient, subject FROM notifications ORDER BY id"` shows one email to the employee (`Your leave request was approved` or `... was rejected`) and one to each active HR account (`Leave request approved` or `Leave request rejected`). Admin accounts get none.

## Gotchas

- Fill date inputs one call at a time. Parallel fills cleared `End date` and removed the weekday summary.
- Approving Medical or Hospitalisation leave that has an attachment opens `window.confirm` (`Have you checked that the attached medical certificate is valid and covers these dates?`). Approving Paternity leave without a birth date opens `window.prompt` (`Enter the verified birth date (YYYY-MM-DD). Paternity leave must start on it.`). Stub them as the skill's Drive section describes.
- Medical, Hospitalisation, Emergency, Marriage and Compassionate leave cannot be approved without an attachment (see [Supporting documents](./supporting-documents.md)).
- New requests must fall in the current calendar year, and Annual leave can only use leave earned in completed months.
- Unsaved timesheet edits block submitting (`Save your timesheet before submitting a request.`) and deciding (`Save your timesheet before reviewing requests.`).
