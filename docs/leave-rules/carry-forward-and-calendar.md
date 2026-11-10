# Carry-forward and shared leave calendar

Recorded 15 October 2026 from section 4.2.9 of the revised leave specification and its diagram, with the company decisions in `LEAVE_POLICY.md`. Claims are outside this work.

## Ordinary decisions

HR decides pending leave requests and pending cancellations. The requestor cannot decide their own request. The decision, its history and its balance movement form one atomic transaction. The employee and HR receive email notifications. There is no HR acknowledgement step.

## Annual Leave carry-forward

1. Remind employees on 1 October and 30 November. A request to carry more than five days needs written approval from the employee's immediate superior, submitted to HR by 15 December. HR verifies and records the approved higher limit and its evidence: a limit above five days, in half-day steps, recorded from 1 October to 15 December, once per source year.
2. New planned Annual Leave requests are not cut off at 15 December (company decision, 25 October 2026). At year-end, take the final approved usage and unused Annual Leave from the closing calendar year.
3. Calculate the normal candidate as the lesser of unused leave and five days (40 hours), or the lesser of unused leave and the HR-recorded written limit. Track the remainder separately as unused leave; do not silently erase it or book it as carry-forward.
4. Show HR a draft summary. HR may reduce the proposed credit in half-day steps, with a mandatory reason and supporting document reference, but never above verified unused leave or the approved limit. Store the original calculation, the reviewed amount, the reason and the document reference. Require explicit HR confirmation, then post one carry-forward credit to the next year's separate carry-forward bucket. Key the operation on employee, source period and target period so retries cannot credit twice.
5. Lock the prior-year balance, notify the employee, show carried-forward and ordinary annual balances separately, and remind about the 31 March use-by date. Expiry and forfeiture postings stay disabled until the statutory and company treatment is validated.
6. For a January–March Annual Leave approval, deduct from the posted carry-forward credit first, then from the current year's earned balance. A request spanning 31 March may use carry-forward only for its dates through 31 March. Record the exact split against the request so a cancellation restores the same two buckets once. A carry-forward credit is not a current-year earned grant.

While HR can still confirm (until 31 March), a January–March Annual Leave approval must stop with an HR-confirmation message if the preceding year left unused hours but has no confirmed carry-forward credit. It must not charge current-year earnings first. After confirmation, later approval or cancellation accounting must not change the prior year.

Notifications: email the employee and HR only when a positive carry-forward credit is created. Employee reminders go out only on 1 October and 30 November.

## Shared leave calendar (23 October 2026)

- Every employee can see who is on leave in an in-app calendar.
- Show only approved absences: employee display name, start/end dates, and a full-day or half-day label per date. Label a four-hour date as a half day without claiming AM or PM.
- Never show leave type, reason, evidence, balance or decision data.
- Write the calendar entry on approval and remove it on approved cancellation, in the same transaction. Employees cannot write calendar entries.
- Ordinary employees must not gain read access to other employees' private leave requests to populate the calendar.
- An organisation-wide Microsoft 365 calendar invitation (`<Name> on leave` to `all@asteriskdigital.com.my`) was considered and deferred. Do not run both channels by default.
