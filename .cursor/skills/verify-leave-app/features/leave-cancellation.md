# Leave cancellation

An employee asks to cancel approved leave whose dates are all in the future. The leave stays Approved until HR decides. `Cancel leave` marks it Cancelled and returns its hours; `Keep leave` keeps it Approved.

## Sub-features

- `cancel-request` records a pending cancellation with a reason.
- `cancel-approve` marks the leave Cancelled and restores the hours.
- `cancel-reject` keeps the leave Approved.
- `emergency-correct` lets HR correct someone else's started Emergency leave (`Correct (cancel) this leave`).

## How to get to it (user POV)

- As the employee, button `Request cancellation` on an approved future leave row, on `Dashboard` or `My leave`.
- As HR, sidebar `HR approvals`, buttons `Cancel leave` and `Keep leave` on the request.

## Driving it with cursor-ide-browser

Preconditions:

- An approved future Annual leave request from [Leave request and approval](./leave-request-approval.md).

- **Request.** Sign in as the employee, open `My leave`. Stub `window.prompt` with the reason (the real prompt reads `Why do you want to cancel this leave? HR will review the cancellation; the leave stays approved until then.`), then click `Request cancellation`. `✓ Cancellation sent to HR for review.` appears; the row stays `Approved` and shows `Cancellation requested: <reason>`.
- **Decide.** Sign in as HR, open `HR approvals`. The row shows `Cancel leave` and `Keep leave`. Click `Cancel leave`: `✓ Leave cancelled and its hours returned.` and status `Cancelled`. `Keep leave` instead shows `✓ Cancellation rejected; the leave stays approved.` No dialog opens for HR.
- **Proof.** `verify sql "SELECT c.kind, c.reason, c.status, r.status FROM cancellations c JOIN requests r ON r.id = c.request_id"` returns `Cancellation  <reason>  Approved  Cancelled`. The outbox gains `Your leave cancellation was approved` and `Leave cancellation approved`.

## Gotchas

- The employee cannot cancel leave whose dates include today or earlier.
- HR's Emergency correction prompt reads `Why does this Emergency leave need correcting? The leave will be cancelled and its Annual Leave hours restored once the correction is approved.`
