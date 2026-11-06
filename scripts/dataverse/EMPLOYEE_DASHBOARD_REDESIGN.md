# Employee dashboard redesign reference

Reference: the user's 24 October 2026 "Leave & Time Off Portal - Redesign" image in this chat. This is a layout reference, not a source of company policy or sample balance data.

## Layout to implement in Employee self service

1. Blue header with "Leave & time off", a short description, and two actions: "Request time off" and "Who's on leave". The first action opens the existing leave-request experience; the second opens the published leave calendar.
2. Annual Leave overview with three equal cards in one row: granted so far, approved/booked, and available. Read the current-year Annual Leave Balance Period and ledger; keep the eight-hour day and four-hour half-day note.
3. Other leave balances as compact full-width rows: Hospitalisation, Maternity, and Paternity. Hospitalisation shows its separate granted, approved/booked, and remaining ledger values, plus the company's combined Medical/Hospitalisation reporting figure. Maternity and Paternity show per-qualifying-birth entitlement and approved booked days, with eligibility pending HR review unless actually verified. Never infer "Eligible" or an available/remaining entitlement from zero usage alone.
4. Recent leave requests with type, dates, duration, status, and a link to the existing request details/edit action. Do not expose approval controls in the employee view.
5. Keep the layout responsive and the bottom actions on one row where space permits. Use real current-year data, not the example's 2025 or fictional people/requests.

## Validation before publishing

- Confirm the annual and Hospitalisation cards against each employee's Dataverse period and ledger, including a nonzero approved debit and cancellation reversal when a genuine eligible request exists.
- Confirm Maternity/Paternity booked days are counted from approved genuine requests and are not treated as annual banks.
- Check the published screen as Darwisyah in Firefox and as Amirul in Chrome at desktop and narrower widths.
- Do not create fictional leave requests for UI testing.

## Published implementation — 25 October 2026

The redesigned Employee self service welcome screen was saved and published in the Developer environment. The published app was refreshed and inspected as Muhammad Amirul in Chrome.

- The annual overview uses three equal cards driven by the existing live balance text: granted so far, used, and available. The live values inspected were 10.5, 1.0, and 9.5 days respectively. The eight-hour/full-day and four-hour/half-day note remains visible.
- Hospitalisation, Maternity, and Paternity are shown as compact rows using the existing calculation formulas. The Maternity/Paternity descriptions now describe Superior review rather than HR verification; neither row claims the signed-in employee is eligible.
- A recent-request table shows the four latest requests for the signed-in employee, with request number, leave type, dates, and status. At narrow widths these render as stacked cards.
- The leave-request and leave-calendar actions remain side by side at the bottom, including the narrow layout. The blue title band is retained.
- No fictional leave requests were created. Maternity, Paternity, and Hospitalisation approval/balance paths were not verified by this UI-only publication.

The current implementation does not include every optional reference detail: the title band does not contain the reference's top actions, and recent requests do not yet show duration or a direct details link. The existing leave-request action below the table remains available. Existing `HtmlText4` delegation warnings for balance-period and ledger lookups remain; this redesign did not change those formulas. Specialized leave values still require verification against genuine approved requests and Dataverse ledger entries.
