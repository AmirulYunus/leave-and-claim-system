# Leave policy and implementation reference

Recorded on 5 October 2026. This records the user's company policy and the official-source research discussed in this chat; it is not a claim that the current application implements these rules. Recheck current law before production implementation.

## Confirmed company decisions

### Next.js application scope — 6 November 2026

- The Next.js application implements the leave requirements below. Detailed
  category, carry-forward, calendar and entitlement-input rules are in
  `docs/leave-rules/`.
- The roles are Employee, HR and Admin. Admin is the developer superuser.
  There is no Superior role: HR approves or rejects leave and claim requests,
  and each decision emails both the employee and HR. Nobody may decide their
  own request.
- Admin creates HR accounts. HR can create HR and Employee accounts.
- Users sign in with an email address and password.
- Supporting documents are stored on local disk in development and in an S3
  bucket in staging and production.
- Claims and timesheets remain part of the Next.js application.

### Remaining leave types and December request timing — 25 October 2026

- Confirmed 26 October 2026: employees may choose Unpaid Leave even if paid
  leave remains. HR approves it under the ordinary decision route; approved
  Unpaid Leave deducts no Annual, Sick, Hospitalisation, or other leave
  balance. Do not enforce the specification's paid-leave-exhaustion
  prerequisite.
- Replacement Leave is deferred at the company's request. Keep it unavailable
  for approval until the company resumes that work and confirms the earning
  and use rules.
- Do not block new planned Annual Leave requests after 15 December. The
  specification's planned-request cut-off is superseded by this direct company
  decision. The separate 15 December deadline for written requests to carry
  more than five days remains in force.
- Build Compassionate, Emergency, Training, Unpaid, Replacement and Off in lieu
  from the leave specification. Keep a type unavailable for approval until its
  complete category-specific evidence, authority, date and accounting
  safeguards are in place. Do not infer earning rates, expiry or credit rules
  where the specification is silent.
- For an unexpected Emergency Leave, the employee may submit the request and
  evidence after the leave. When approved, its used hours must be charged to
  the employee's Annual Leave balance. Ordinary Annual Leave request timing
  and behavior stay as currently implemented; do not apply the PDF's
  three-day-before wording to Emergency Leave.
- HR may correct an approved Emergency Leave after its dates have passed when
  the approval was made in error. The correction requires a recorded reason
  and restores the exact Annual Leave and carry-forward hours originally
  charged, once. The employee cannot use this exception to reverse past
  leave; ordinary approved-future cancellation remains separate.
- Keep Off in lieu unavailable until HR sets the earning amount and expiry
  rules for approved extra work. Do not create credits from an assumed rate.

### Marriage Leave rule from the revised leave specification — 22 October 2026

- The first legal marriage qualifies for three consecutive calendar days after
  probation has been completed. The employee supplies the official registration
  date and marriage certificate and declares that it is their first legal
  marriage; HR verifies the document and declaration.
- All three leave days must fall after registration and within twelve months
  of the official registration date. An existing approved Marriage Leave
  prevents a second approval; a properly cancelled future request can be
  rebooked within the same eligibility window.

### Sick Leave evidence and notification — 18 October 2026

- A medical certificate must be uploaded before Sick Leave can be approved.
- The employee must submit the Sick Leave request in the app within 48 hours
  of the leave starting; an earlier message to a manager does not substitute
  for the app request. For a date-only Start Date, the clock starts at 00:00
  Malaysia time on that date: a Monday start must be submitted by Wednesday
  00:00 Malaysia time. An uploaded file alone does not prove that it is a
  valid certificate; HR must verify it.

### Hospitalisation balance interpretation — 19 October 2026

- HR confirmed on 19 October 2026 that the company's document intends the
  60-day Hospitalisation allowance to include Medical Leave already taken.
  This confirms the requested company accounting rule; it does not resolve its
  apparent conflict with the statutory minimum. Current official Employment
  Act materials describe non-hospitalisation Sick Leave (14/18/22 days, by
  service) and 60 days of hospitalisation Sick Leave as separate entitlements.
  A hard combined 60-day approval cap could reduce statutory leave. Implement
  the combined figure for company reporting, while preserving the separate
  60-day statutory Hospitalisation floor for eligibility and approval. The
  reporting figure must never be the sole approval limit.
- Hospitalisation approval requires an uploaded medical certificate, an HR
  decision and an existing current-year Hospitalisation grant. The decision,
  ledger deduction and history commit in one transaction; approved future
  cancellation restores the original debit once. HR must verify the
  certificate's content. Granting the current-year Hospitalisation period is
  idempotent.
- Example: after 14 days of ordinary Medical Leave, the company document would
  show 46 of its combined 60 days left, while JTKSM describes 60 days of
  Hospitalisation Leave excluding ordinary Sick Leave.
  See JTKSM's [2025 paid-sick-leave infographic](https://jtksm.mohr.gov.my/sites/default/files/2025-04/Infografik%202025%20%281%29.pdf)
  and [Employment Act 1955 section 60F](https://jtksm.gov.my/sites/default/files/2023-11/Akta%20Kerja%201955%20%28Akta%20265%29.pdf).

### Employee request form clarification — 16 October 2026

- Every leave category should use one main request form for both creation and
  permitted pending edits. The server generates and validates the appropriate
  dates for each category; employees should not need a second page to submit
  a request.
- The maternity End Date should be filled from the Start Date as an inclusive
  98-calendar-day period (Start Date + 97 days). The paternity End Date should
  be an inclusive seven-calendar-day period (Start Date + 6 days). Display
  these calculated dates on the main form and enforce them on the server.
- Maternity requests capture the expected confinement date and formal-notice
  date. HR checks eligibility and notice when deciding; a separate HR
  verification timestamp is not a prerequisite for approval. Do not reject a
  request solely because its notice dates fail a rigid 60-day formula: the
  [Employment Act 1955, section 40](https://jtksm.mohr.gov.my/sites/default/files/2023-11/Akta%20Kerja%201955%20%28Akta%20265%29.pdf)
  contains qualifications that require HR assessment.
- A past-year Date Joined must be accepted for employee profiles. Do not
  substitute the current year when HR supplies an earlier joining date.
- Verify that supporting files are actually saved and visible on each request.
  A selected file on the screen is not evidence that the server stored it.

### Clarifications from the 15 October 2026 revised leave specification

- Ordinary leave approval or rejection takes effect with the authorized
  decision; HR receives an email notification and does not acknowledge it.
  The same notification-only treatment applies to ordinary cancellation.
  Year-end Annual Leave carry-forward is a separate workflow and follows
  section 4.2.9 of `Leave Management and Claim Management - Process Flow and
  Functional Specification.pdf`: October and November reminders, 15 December
  special-limit approval deadline (the planned-leave request cut-off was
  superseded by the 25 October company decision above),
  year-end calculation, HR review and confirmation before posting, then
  separate carried-forward/unused amounts, notification, duplicate prevention
  and prior-year lock. The standard carry-forward limit is five days; a higher
  limit needs written approval from the employee's immediate superior,
  verified and recorded by HR. The document specifies use by 31 March.
  Implement statutory/policy validation before any forfeiture or expiry
  posting. See `docs/leave-rules/carry-forward-and-calendar.md`.
- For Annual Leave approved for dates from 1 January through 31 March, spend
  available carried-forward leave first, then the current year's earned
  Annual Leave. Dates after 31 March must not spend the carried-forward bucket.
  An approved cancellation must restore the exact buckets originally charged.
- HR authority is enforced on the server from the signed-in account's role;
  configured email recipients alone are not authorization. The self-approval
  prohibition and one-decision accounting apply to both leave requests and
  pending cancellations, with exact-once reversal.

### Shared absence calendar — 23 October 2026

- Colleagues can see who is on leave in an in-app calendar. This was chosen
  over organization-wide Microsoft 365 invitations because it needs no
  delegated access to every mailbox; do not run both channels by default.
- Show approved absences only, with the employee's display name and covered
  dates. Label a four-hour date as a half day without claiming AM or PM. Keep
  leave type, reason, certificate, attachments, balance and decision comments
  out of the shared view. Remove the absence after approved cancellation.
- Serve the calendar from data limited to those fields. Do not give ordinary
  employees read access to other employees' private leave requests.

### First-year annual-leave proration example — 14 October 2026

**Clarified 3 November 2026:** Earned leave counts only calendar months that
have ended. On 3 November, November is still incomplete, so a February join
has earned February through October (nine months), subject to the confirmed
joining-month convention below. November is credited on 1 December, after the
entire month has finished.
This completed-month rule applies to both the grant and the approval cap;
future leave dates cannot make an unfinished month available early.
Earlier ledger credits remain recorded for audit; they do not increase the
server-side approval cap. A displayed ledger total must be identified as
recorded credit, not usable earned leave, until reconciled.

The user distinguished **calendar-year entitlement** from **leave earned to
date**. For a 14-day full-year annual rate and a February join, the stated
example counts February through December as 11 months:
`14 / 12 × 11 = 12.8333… days` (shown as 12.83). Through October it counts
February through October as 9 months:
`14 / 12 × 9 = 10.5 days`. The available balance would then depend on the
earned amount and approved usage, not merely the full-year entitlement.

The user confirmed that a joining month counts as a full month even when the
employee starts after its first day. There is no daily proration of that month.
The user also chose to round each **cumulative** prorated amount up to the next
half-day, leaving exact half-days unchanged. Thus 12.8333… days becomes 13
days (104 hours), and 10.5 days remains 10.5 days (84 hours). Calculate from
the annual rate and total eligible months before rounding; do not round each
monthly increment separately, which could inflate the year-end result.
The user also confirmed that employees may use **only leave earned so far**.
An October request for a future date cannot consume October, November or December's
unearned credit. Approved usage reduces the currently earned balance. A
full-year entitlement remains informational until its months are earned.
This confirms the example's month-counting and conceptual distinction; it does
not settle the last-year rule. Keep last-year proration held until that rule
and its tests are complete. Do not treat the displayed 12.83 as a 12.83-day
ledger credit; the rounded year-end amount is 13 days.

### Employee annual-leave request UX clarification — 12 October 2026

The user wants one From Date, one To Date, and one Half Day (4 hours) or Full
Day (8 hours) selection when creating an Annual Leave request. That duration
should apply uniformly to all included dates. For a pending request, the
employee should be able to edit the From/To dates and choose one 4/8-hour
duration for the resulting dates.
For Annual Leave, include every Monday–Friday date in the inclusive From/To
range and skip Saturdays and Sundays. The user confirmed this on 12 October.
There is no configured holiday calendar or automatic holiday exclusion;
weekdays that happen to be public holidays need employee attention until
another rule is confirmed. A pending request may be edited, but approved
requests use the separate future cancellation process. Do not silently convert
existing mixed-duration requests to a uniform duration.

### Clarifications 10 October 2026

- The intended HR members are KhorLord.Yong@Asteriskdigital.com.my and
  Jennie.Cheong@Asteriskdigital.com.my. Admin identity remains separate.
- Everyone may see which employees are on leave in a shared calendar (see the
  23 October decision). Reasons, certificates, private decisions and balances
  are not approved for public disclosure.
- Remove the separate HR/Admin acknowledgement workflow: notification only,
  no acknowledgement queue, button, identity/time record or condition delaying
  approval/cancellation. This supersedes the Word acknowledgement requirement.
- Notifications are email only.
- No holiday-calendar configuration or automatic holiday exclusion feature is
  required. Keep date order, range, duplicate-date and duration checks. This
  does not change entitlements or consecutive-calendar-day rules for other
  categories.

### Implementation decisions 9–10 October 2026

- Leave rules come from the specification's Leave Management module.
- HR maintains the employment details needed for entitlement calculations:
  joining date, position/level, employment type and applicable probation
  details.
- If any input required for an entitlement calculation is null, skip that
  employee's calculation until HR supplies it. Represent this as not calculated
  / pending HR details, rather than zero entitlement. Do not invent defaults or
  treat missing data as proof of ineligibility. Preserve existing balance/history
  while calculation is incomplete. Required inputs are specific to the
  calculation; optional or unrelated fields do not determine an allowance.
- Service-based annual allowance changes take effect on each completed-service
  anniversary. Use the Malaysia calendar date when assessing service.
  Proration and credits remain separate rules; a full-year rate is not an
  available balance.
- Confirmed 10 October 2026: annual-leave balance periods are calendar years,
  1 January through 31 December. Allowance increases still take effect on
  service anniversaries.
- The document's position-based annual leave and service rewards, and
  4/8-hour annual leave durations, supersede the earlier universal 14-day and
  flexible-hour company rules below. Preserve applicable statutory minimums.
- Long-service rewards from 8 years onward, last-year proration, and
  cancellation of already-taken/in-progress leave are on hold. Do not infer
  defaults for these cases.
- Future approved-leave cancellation: the employee requests it, HR decides,
  the original stays Approved with its deduction while pending, then becomes
  Cancelled with an exact reversal on approval, with decision history. HR
  receives email notification only.

### Original decisions — 5 to 7 October 2026

Some of these are superseded by the later decisions above; superseded items
are marked.

- The company is based in Kuala Lumpur. Scope this system to Peninsular Malaysia's Employment Act 1955; Sabah and Sarawak rules are outside the requested scope.
- Everyone starts with 14 days of annual leave (AL). This is the company baseline, not the statutory service-band table. *(Superseded by the position-based 14/14/15/18-day base.)*
- Preserve the statutory floor: employees with five or more years of service must receive at least 16 days of annual leave. Do not cap them at 14 days.
- Employees must be able to cancel their leave requests (confirmed 7 October 2026).
- Normal schedule is Monday–Friday, eight working hours per day, excluding lunch (confirmed in chat).
- Add a monthly timesheet table of calendar dates. Weekend hours default to zero; employees enter actual worked hours on weekdays (normally eight).
- Employees may request from one to eight annual leave hours per working date, including fractional hours. *(Superseded by exactly 4 or 8 hours per date.)*
- On this schedule a 14-day allowance is 112 hours and the 16-day statutory floor is 128 hours. Retain the underlying day entitlement and use hours for partial-day accounting.
- Worked hours and leave hours are separate entries. Approved leave should populate the timesheet; pending leave must remain distinguishable and must not be treated as approved leave.
- Maternity and paternity leave retain their statutory consecutive-calendar-day rules; do not convert every leave category to hourly annual-leave accounting.

## Statutory baseline researched

Assumption: ordinary private-sector employees under contracts of service. Check any special employee-category exclusions where applicable. Salary above RM4,000 does not by itself remove these leave entitlements.

| Continuous service with the same employer | Minimum annual leave per 12 months of service | Ordinary paid sick leave per calendar year | Separate hospitalisation leave per calendar year |
| --- | --- | --- | --- |
| Less than 2 years | 8 days | 14 days | 60 days |
| 2 years to less than 5 years | 12 days | 18 days | 60 days |
| 5 years or more | 16 days | 22 days | 60 days |

- Annual leave is additional to rest days and paid public holidays. Use the employee's confirmed work schedule and holiday calendar rather than inclusive calendar-day counting.
- Annual leave generally must be taken within 12 months after the relevant 12-month service period ends. Do not assume all balances expire on 31 December. Confirm company carry-forward and accrual arrangements against this requirement.
- Sick leave requires qualifying medical certification and informing or attempting to inform the employer within 48 hours of its commencement. Hospitalisation leave is a separate entitlement; certification can qualify an employee even when actual hospital admission does not occur.
- Maternity leave is 98 consecutive days. Assess maternity allowance eligibility separately, including statutory service and other conditions; do not automatically treat every leave entitlement as allowance eligibility.
- Paternity leave is seven consecutive paid days, including rest days and public holidays, subject to marriage, at least 12 months with the same employer, notification requirements, and a maximum of five confinements.
- Public holiday entitlement includes 11 paid gazetted holidays, of which National Day, Agong's Birthday, Federal Territory Day for KL, Labour Day, and Malaysia Day are compulsory. Additional holidays appointed under section 8 of the Holidays Act 1951 and applicable substitution rules must also be handled.
- HR review must respect statutory eligibility and entitlements; a generic approve/reject button must not make protected leave entirely discretionary.

## Still unconfirmed

- Whether users can hold multiple roles.
- Cancellation rules for leave that is already taken or in progress, and withdrawal of requests that have not received final approval.
- Which weekend day is the designated statutory weekly rest day, and any employee-specific schedule exceptions.
- Timesheet submission/approval process and overtime handling.
- Carry-forward expiry/forfeiture arrangements and last-year proration.
- Any company annual leave increases above the baseline and statutory floor.
- The company's selected additional public holidays and any benefits beyond statutory minima.

Do not invent these policies.

## Official sources

- [Employment Act 1955, especially sections 37, 60D, 60E, 60F and 60FA](https://jtksm.mohr.gov.my/sites/default/files/2023-11/Akta%20Kerja%201955%20%28Akta%20265%29.pdf)
- [JTKSM Employment Act amendment FAQ](https://jtksm.mohr.gov.my/ms/soalan-lazim/akta-kerja-1955-pindaan-2022)

Read the full relevant provisions when implementing edge cases such as termination, overlapping leave, allowance eligibility, substitutions, and unpaid absence.
