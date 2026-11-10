# Expense claims

An employee submits an expense claim with a category, description and amount. It stays Pending under My claims until another HR or Admin account approves or rejects it under HR approvals, which emails the employee and HR.

## Sub-features

- `claim-submit` creates a Pending claim request with a `claims.amount` row.
- `claim-approve` and `claim-reject` record the HR decision and queue the claim emails.
- `claim-self` blocks deciding your own claim (`Another reviewer decides`).

## How to get to it (user POV)

- Dashboard button `Submit a claim Work expenses, taken care of ↗`, or `Submit claim` on `My claims`.
- As HR, sidebar `HR approvals <pending count>`; claims share the `Employee requests` list with leave.

## Driving it with cursor-ide-browser

Preconditions:

- Signed in as `amirul@example.test`.

- **Submit.** Click `Submit a claim Work expenses, taken care of ↗`. Dialog `Submit an expense claim.` opens. Choose combobox `Category` (`Travel`, `Meals`, `Office supplies`, `Other`), fill `Description` (for example `Client taxi`), `Amount (RM)` (`42.50`) and `Additional details` (`verify-claim proof`). Click `Submit for approval`. `✓ Claim saved for HR review.` appears; `My claims` lists the claim as `Pending`.
- **Decide.** Sign in as HR, open `HR approvals`, click `Approve` on the claim row. `✓ Request approved and saved.` appears. No dialog opens.
- **Proof.** `verify sql "SELECT c.amount, r.status FROM claims c JOIN requests r ON r.id = c.request_id WHERE r.note = 'verify-claim proof'"` returns `42.50  Approved`. The outbox gains `Your claim was approved` and `Claim approved`.

## Gotchas

- The server accepts RM 0.01 to RM 100,000 with at most two decimals; the client only rejects zero or less.
- Claims have no attachment control.
