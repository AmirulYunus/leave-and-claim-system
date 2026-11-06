# Leave policy and implementation reference

Recorded on 5 October 2026. This records the user's company policy and the official-source research discussed in this chat; it is not a claim that the current demo implements these rules. Recheck current law before production implementation.

## Confirmed company decisions

### Next.js application scope — 6 November 2026

- The Power Apps/Dataverse solution is live in production and is no longer
  maintained from this repository. Treat `scripts/dataverse/` as a read-only
  record of the leave requirements; do not deploy, extend or test against
  Dataverse.
- The Next.js application must implement the leave requirements defined for
  Dataverse, adapted to the decisions below. Where an earlier section names a
  Superior, Entra identity, Power Automate flow or Dataverse role, apply the
  rule to the Next.js roles instead.
- The roles are Employee, HR and Admin. Admin is the developer superuser.
  There is no Superior role: HR approves or rejects leave and claim requests,
  and each decision emails both the employee and HR. The existing
  self-approval prohibition still applies.
- Admin creates HR accounts. HR can create HR and Employee accounts. This
  supersedes the 10 October IT-provisioning decision for the Next.js
  application.
- Users sign in with an email address and password.
- Supporting documents are stored on local disk in development and in an S3
  bucket in staging and production.
- Claims and timesheets remain part of the Next.js application.

### Remaining leave types and December request timing — 25 October 2026

- Confirmed 26 October 2026: employees may choose Unpaid Leave even if paid
  leave remains. The Superior approves it under the ordinary decision route;
  approved Unpaid Leave deducts no Annual, Sick, Hospitalisation, or other
  leave balance. Do not enforce the specification's paid-leave-exhaustion
  prerequisite or HR-only decision route for this category.
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
  safeguards are deployed. Do not infer earning rates, expiry or credit rules
  where the specification is silent.
- For an unexpected Emergency Leave, the employee may submit the request and
  evidence after the leave. When approved, its used hours must be charged to
  the employee's Annual Leave balance. Ordinary Annual Leave request timing
  and behavior stay as currently implemented; do not apply the PDF's
  three-day-before wording to Emergency Leave.
- HR may correct an approved Emergency Leave after its dates have passed when
  the approval was made in error. The correction requires a recorded reason
  and restores the exact Annual Leave and carry-forward hours originally
  charged, once. A Superior or the employee cannot use this exception to
  reverse past leave; ordinary approved-future cancellation remains separate.
- Keep Off in lieu unavailable until HR sets the earning amount and expiry
  rules for approved extra work. Do not create credits from an assumed rate.

### Marriage Leave rule from the revised leave specification — 22 October 2026

- The first legal marriage qualifies for three consecutive calendar days after
  probation has been completed. The employee supplies the official registration
  date and marriage certificate and declares that it is their first legal
  marriage; the approver verifies the document and declaration.
- All three leave days must fall after registration and within twelve months
  of the official registration date. An existing approved Marriage Leave
  prevents a second approval; a properly cancelled future request can be
  rebooked within the same eligibility window.
- The rule is being implemented in the Developer environment. Local validation
  alone does not mean the category is available in the published app.

### Sick Leave evidence and notification — 18 October 2026

- A medical certificate must be uploaded before Sick Leave can be approved.
- The employee must submit the Sick Leave request in the app within 48 hours
  of the leave starting; an earlier message to a manager does not substitute
  for the app request. For a date-only Start Date, the clock starts at 00:00
  Malaysia time on that date: a Monday start must be submitted by Wednesday
  00:00 Malaysia time. An uploaded file alone does not prove that it is a valid certificate;
  the approver must verify it.

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
  reporting figure must never be the sole approval limit. In the Developer
  environment, Hospitalisation approval now requires an uploaded medical
  certificate, an authorized Superior or HR decision, and an existing
  current-year Hospitalisation grant. The decision, ledger deduction and
  history commit in one transaction; approved future cancellation restores
  the original debit once. The approver must verify the certificate's content.
  Example: after 14 days of ordinary Medical Leave, the company document would
  show 46 of its combined 60 days left, while JTKSM describes 60 days of
  Hospitalisation Leave excluding ordinary Sick Leave.
  The Developer HR Employee form now shows the combined figure after recorded
  Medical and Hospitalisation use. It is a read-only reporting figure, not an
  approval limit. HR can grant the separate current-year Hospitalisation
  period on the employee's Annual balance tab; the action is idempotent.
  See JTKSM's [2025 paid-sick-leave infographic](https://jtksm.mohr.gov.my/sites/default/files/2025-04/Infografik%202025%20%281%29.pdf)
  and [Employment Act 1955 section 60F](https://jtksm.gov.my/sites/default/files/2023-11/Akta%20Kerja%201955%20%28Akta%20265%29.pdf).

### Employee request form clarification — 16 October 2026

- Every leave category should use one main request form for both creation and
  permitted pending edits. Remove the separate leave-date entry page once the
  server generates and validates the appropriate dates for each category.
  Employees should not need to complete a second page to submit a request.
- The maternity End Date should be filled from the Start Date as an inclusive
  98-calendar-day period (Start Date + 97 days). The paternity End Date should
  be an inclusive seven-calendar-day period (Start Date + 6 days). Display
  these calculated dates on the main form and enforce them on the server.
- Maternity requests capture the expected confinement date and formal-notice
  date. The specification names the Superior as approver and does not require a
  separate HR verification step. The approver checks eligibility and notice;
  HR may assist, but an HR verification timestamp is not a prerequisite for
  approval. Do not reject a
  request solely because its notice dates fail a rigid 60-day formula: the
  [Employment Act 1955, section 40](https://jtksm.mohr.gov.my/sites/default/files/2023-11/Akta%20Kerja%201955%20%28Akta%20265%29.pdf)
  contains qualifications that require HR assessment.
- A past-year Date Joined must be accepted for employee profiles. Do not
  substitute the current year when HR supplies an earlier joining date.
- Verify that supporting files are actually saved and visible on each request,
  including requests submitted by Darwisyah. A selected file on the screen is
  not evidence that Dataverse retained it.

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
  limit needs written immediate-Superior approval and HR verification. The
  document specifies use by 31 March. Implement statutory/policy validation
  before any forfeiture or expiry posting.
- For Annual Leave approved for dates from 1 January through 31 March, spend
  available carried-forward leave first, then the current year's earned
  Annual Leave. Dates after 31 March must not spend the carried-forward bucket.
  An approved cancellation must restore the exact buckets originally charged.
- An authorized HR member may approve or reject a pending employee leave
  request regardless of whether its assigned Superior is absent. This lifts
  the earlier HR-override hold and does not require an absence/delegation
  state. HR authority must be resolved from verified tenant Entra identities
  for the designated members and enforced on the server; configured email
  recipients alone are not authorization. The self-approval prohibition and
  one-decision accounting still apply. The exact same authority should be
  applied to pending cancellation with the same self-decision prohibition and
  exact-once reversal. The Developer server and form paths were updated on
  19 October 2026; own-session HR sign-in remains a release check.
- After a final Superior or HR approval, create an organization-visible
  Microsoft 365 calendar invitation from the approved employee's mailbox for
  the leave period. Send it to `all@asteriskdigital.com.my` with the subject
  `<Name> on leave`; never include reason, medical evidence, attachments or
  private balance. The employee-mailbox automation needs IT-approved Graph
  calendar access before deployment. Calendar creation must be idempotent and
  approved cancellation must cancel the original invitation.

### Employee-app absence calendar choice — 23 October 2026

- The user is open to an in-app calendar instead of organization-wide meeting
  invitations, provided colleagues can see who is on leave. Implement the
  employee-app calendar first because it does not require delegated access to
  every employee mailbox or send an invitation to every colleague.
- Show approved absences only, with the employee's display name and covered
  dates. Label a four-hour date as a half day without claiming AM or PM. Keep
  leave type, reason, certificate, attachments, balance and decision comments
  out of the shared view. Remove the absence after approved cancellation.
- Use a dedicated read-only public absence projection. Do not broaden ordinary
  Employee Read access on the private Leave Request table to populate it.
- The Microsoft 365 invitation design above remains deferred while this
  employee-app approach is implemented; do not run both channels by default.

### First-year annual-leave proration example — 14 October 2026

**Clarified 3 November 2026:** Earned leave counts only calendar months that
have ended. On 3 November, November is still incomplete, so a February join
has earned February through October (nine months), subject to the confirmed
joining-month convention below. November is credited on 1 December, after the
entire month has finished.
This completed-month rule applies to both the daily grant and the approval cap;
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
not settle the last-year rule. Keep last-year live grants held until that rule
and its tests are complete. Do not treat the displayed 12.83 as a 12.83-day
ledger credit; the rounded year-end amount is 13 days.

### Employee annual-leave request UX clarification — 12 October 2026

The user wants one From Date, one To Date, and one Half Day (4 hours) or Full
Day (8 hours) selection when creating an Annual Leave request. That duration
should apply uniformly to all included dates. For a pending request, the
employee should be able to edit the From/To dates and choose one 4/8-hour
duration for the resulting dates. The current separate Add leave dates flow and
mixed 4/8-hour entries in one request do not match this requested experience.
For Annual Leave, include every Monday–Friday date in the inclusive From/To
range and skip Saturdays and Sundays. The user confirmed this on 12 October.
The earlier decision remains that there is no configured holiday calendar or
automatic holiday exclusion; weekdays that happen to be public holidays need
employee attention until another rule is confirmed. A pending request may be
edited under the new workflow, but existing approved requests retain the
separate future cancellation process. Do not silently convert existing mixed-
duration requests to a uniform duration.

### Latest clarifications 10 October 2026

These user decisions supersede conflicting acknowledgement, notification,
HR-identity, holiday-automation and account-provisioning assumptions below.
They record intended behavior, not a claim of live access-control or calendar
deployment.

- The people listed in the existing HR email environment variable are the
  intended HR members: KhorLord.Yong@Asteriskdigital.com.my and
  Jennie.Cheong@Asteriskdigital.com.my. HR membership is no longer unspecified.
  Resolve configured addresses to verified tenant Entra identities for server
  authorization; do not trust client email/display-name assertions. Actual
  Dataverse HR permissions and authority mapping remain undeployed/unverified.
  Admin identity remains unspecified and separate. The HR approval override is
  not enabled by recording this membership decision.
- Everyone may see which employees are on leave in a shared calendar. Prefer
  presentation in Teams; the 15 October clarification above raises this work's
  priority and specifies organization-wide event invitations. Default content is approved absence,
  employee name and dates/duration; reasons, certificates, private decisions
  and balances are not approved for public disclosure. Exact Teams integration
  and destination team/channel require later verification. No calendar events
  or invitations have been published.
- Remove the separate HR/Admin acknowledgement workflow: notification only,
  no acknowledgement queue, button, identity/time record or condition delaying
  approval/cancellation. This supersedes the Word acknowledgement requirement.
- Notifications are email only. Teams calendar presentation is separate from
  notification delivery; do not add Teams message notifications by default.
- No holiday-calendar configuration or automatic holiday exclusion feature is
  required. Employees choose the actual dates on which they need leave. Annual
  accounting must use explicit saved per-date 4/8-hour entries, not every date
  in an inclusive start/end span. Keep date order, range, duplicate-date and
  duration checks. This implementation choice does not change entitlements or
  consecutive-calendar-day rules for other categories.
- Account provisioning remains with IT. Remove employee/account creation from
  the HR app's intended scope: directory sync creates/links Employee profiles,
  and HR edits existing employment details needed for entitlement calculations.
  This supersedes the earlier HR account-creation requirement. Removing the
  native HR New command and enforcing the permissions remain implementation work.
- In the Superior app, a Superior may view all Employee profiles but must not
  update any Employee information. Leave Approvals and Leave Cancellations must expose
  only records for employees whose latest synced Superior lookup points to that
  Superior. Apply this at the Dataverse access layer as well as in app views;
  a filtered screen alone is not sufficient. This requirement was confirmed on
  14 October 2026 and awaits a separate ordinary Superior account for testing.
- The System administration app is unused. The user requested an opinion on
  deleting it; app retirement is recommended, but deletion has not been carried
  out. Keep IT's platform administration capabilities and shared data/flows;
  retiring this app does not grant HR administrator access.

### Earlier implementation decisions 9–10 October 2026


- Implement the Word specification's Leave Management module; Claim Management is excluded.
- Latest correction after inspecting sync inputs: HR actions are required to
  maintain employee employment details that the sync does not provide. Restore
  HR maintenance of joining date, position/level, employment type and applicable
  probation details to scope. This supersedes the earlier sync-only employee
  profile instruction. Keep directory identity and Superior under the daily
  sync; the sync must not overwrite HR-maintained employment inputs.
- If any input required for an entitlement calculation is null, skip that
  employee's calculation until HR supplies it. Represent this as not calculated
  / pending HR details, rather than zero entitlement. Do not invent defaults or
  treat missing data as proof of ineligibility. Preserve existing balance/history
  while calculation is incomplete. Interpretation: required inputs are specific
  to the calculation; optional or unrelated fields do not determine an allowance.
  Source ownership, HR-only write enforcement and calculation behavior are
  requirements, not claims that they have been deployed. Privileged identity
  mapping remains subject to the existing hold below.
- Confirmed during entitlement development: service-based annual allowance
  changes take effect on each completed-service anniversary. Use the Malaysia
  calendar date when assessing service. Proration and credits remain separate
  rules; a full-year rate is not an available balance. The later calendar-year
  balance decision is recorded below.
- Confirmed 10 October 2026: annual-leave balance periods are calendar years,
  1 January through 31 December. Allowance increases still take effect on service
  anniversaries. This does not settle carry-forward, expiry, or first/last-year
  proration and rounding; those must not be inferred from the period boundary.
- The document's defined position-based annual leave and service rewards, and
  4/8-hour annual leave durations, supersede the earlier universal 14-day and
  flexible-hour company rules below. Preserve applicable statutory minimums.
- Superior can decide only assigned underlings' requests. The former HR
  approval hold is superseded by the 15 October clarification above. Do not
  enable broad approval through email/name matching; verify tenant identities.
- Long-service rewards from 8 years onward, first/last-year proration and
  rounding, cancellation of already-taken/in-progress leave, and absence
  delegation are explicitly on hold. Do not infer defaults for these cases.
- The documented future approved-leave cancellation path is in scope: request,
  Superior decision, retain original Approved/deduction while pending, then
  Cancelled and exact reversal on approval, with decision history. HR receives
  email notification only; there is no HR acknowledgement action. Cancellation
  of taken or in-progress leave remains on hold.
- IT alone provisions sign-in accounts. The HR app does not create Employee
  accounts, and the unused Admin app is outside the current rollout. The two HR
  email configuration values are notification recipients; they do not grant
  Dataverse access or establish an HR role.

Live deployment progress, including server decision safeguards and calendar-date
conversion, is recorded in `scripts/dataverse/IMPLEMENTATION_STATUS.md`. The user
confirmed on 9 October that the current Power Apps data is dummy and directed
that backup work stop while development continues. This changes development
handling, not leave entitlements or privileged-role policy.

The dated decisions below are retained as history where superseded by this
instruction. This records desired behavior, not a claim that it is deployed.

- The company is based in Kuala Lumpur. Scope this system to Peninsular Malaysia's Employment Act 1955; Sabah and Sarawak rules are outside the requested scope.
- Everyone starts with 14 days of annual leave (AL). This is the company baseline, not the statutory service-band table.
- Preserve the statutory floor: employees with five or more years of service must receive at least 16 days of annual leave. Do not cap them at 14 days.
- Latest Power Apps workflow (confirmed by the user's previous-chat summary on 8 October 2026): the employee's current Microsoft Entra manager approves or rejects inside the app. HR is notified after superior approval and does not approve. Accepted prototype compromise (8 October 2026): the daily employee-sync flow reads Entra managers and stores the Employee Superior lookup. The form follows that lookup and compares the Superior Employee Entra ID with the signed-in systemuser Entra ID on load and again before saving a decision. Manager changes take effect after the next successful sync, which may also be run manually. This is not live Entra validation and provides form-level enforcement only. A replacement manager must be able to decide existing pending requests. Record the actual decision-maker and timestamp. This supersedes the older two-stage Superior/HR approval policy; the local Next.js implementation may still reflect that older workflow. Server-side validation in the Power Apps prototype remains unimplemented.
- Employees must be able to cancel their leave requests (confirmed 7 October 2026). Cancellation handling under the latest single-decision workflow is unconfirmed. The earlier Superior-then-HR cancellation design is historical and must not be assumed to apply to the changed workflow; do not implement new cancellation transitions or balance restoration rules without confirmation.
- The system has four roles: Employee, Superior, HR, and Admin. Employee accesses their own requests and timesheets. Superior is the current Entra manager for request decisions. HR receives approval notifications, manages employee records, and can create new employee accounts. Admin is the superuser with full access, including account and role management. HR and Admin remain separate roles.
- HR's confirmed account-creation permission covers employee accounts. Permission to create HR or Admin accounts has not been granted; keep privileged role assignment under Admin unless the user changes this policy.
- Normal schedule is Monday–Friday, eight working hours per day, excluding lunch (confirmed in chat).
- Add a monthly timesheet table of calendar dates. Weekend hours default to zero; employees enter actual worked hours on weekdays (normally eight).
- Support partial-day annual leave through hours per date, alongside multi-date requests. Employees may request from one to eight annual leave hours per working date, including fractional hours within that range; there is no four-hour increment requirement. Four leave hours equal half a standard working day; eight equal one day. (Minimum updated from any positive amount to one hour on 7 October 2026.)
- On this schedule the 14-day starting AL allowance is equivalent to 112 hours; the 16-day statutory floor is equivalent to 128 hours. Retain the underlying day entitlement and use hours for partial-day accounting.
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

- Whether users can hold multiple roles, and delegation/absence handling. For Power Apps request decisions, Superior authority in the accepted Power Apps prototype comes from the latest successfully synced Superior lookup; delegation remains unconfirmed.
- Cancellation rules for leave that is already taken or in progress, and withdrawal of requests that have not received final approval.
- Which weekend day is the designated statutory weekly rest day, and any employee-specific schedule exceptions.
- Timesheet submission/approval process, overtime handling, and rounding increments for hourly leave.
- Company carry-forward limits, expiry arrangements, and accrual/proration rules.
- Any company annual leave increases above the baseline and statutory floor.
- The company's selected additional public holidays and any benefits beyond statutory minima.

## Development-only testing exception

- During the initial Power Apps prototype, the developer may temporarily use the same Microsoft account as the employee and assigned Superior because other tenant accounts are unavailable for sign-in. This is test data only and does not change the production approval policy. Before deployment, use separate accounts and prevent requestors from approving their own requests.

### Live request testing accounts — 21 October 2026

- For subsequent live Developer-environment workflow tests, do not create or use new fictional Employee profiles or fictional leave-request records. Darwisyah submits clearly marked test requests through her own work account in Firefox; Muhammad Amirul reviews them through his own work account in Chrome, as her current Microsoft Entra Superior. Confirm the latest Employee Superior sync before testing an approval or rejection.
- Keep live test requests distinguishable from genuine leave. If an approval changes Darwisyah's balance, verify the deduction and complete the authorized cancellation/reversal test so the test does not leave an unintended balance change.
- Darwisyah currently has combined Employee, Superior, HR and Basic User roles. Her session can test the workflow, but it cannot establish that an Employee-only account is correctly restricted. Test that boundary separately with an Employee-only sign-in before production rollout. Existing historical fictional fixtures are not changed by this testing instruction.

Do not invent these policies. The preview now uses the 14/16-day company annual allowance, service-based sick leave, separate hospitalisation entitlement, per-date annual leave hours, and monthly worked-hour inputs. Join dates and the 2026 balance period are sample data. Public holidays, carry-forward, accrual, and statutory certification verification remain unimplemented. Requests, HR decisions, and saved timesheet hours now persist in local PostgreSQL; real login and production access control remain unimplemented. Maternity and paternity request workflows remain deferred until their eligibility checks are implemented.

## Official sources

- [Employment Act 1955, especially sections 37, 60D, 60E, 60F and 60FA](https://jtksm.mohr.gov.my/sites/default/files/2023-11/Akta%20Kerja%201955%20%28Akta%20265%29.pdf)
- [JTKSM Employment Act amendment FAQ](https://jtksm.mohr.gov.my/ms/soalan-lazim/akta-kerja-1955-pindaan-2022)

Read the full relevant provisions when implementing edge cases such as termination, overlapping leave, allowance eligibility, substitutions, and unpaid absence.
