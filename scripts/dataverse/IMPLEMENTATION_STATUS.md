# Live Leave Management implementation status

## Remaining categories — Training and Unpaid Leave Developer deployment, 26 October 2026

Training Leave has a required category (`Company Required` or
`Employee-Requested External`). The local approval guard checks the selected
category and exact 4/8-hour weekday rows. Company-required training has no
Annual debit; employee-requested external training uses the existing earned
Annual and carry-forward allocation, with an exact future-cancellation reversal.
The Training Category column, employee selector, Superior review field and
signed server assemblies were deployed to the Developer environment. The
employee app reports a successful publish, and the Leave Type readback shows
one active Training Leave record (`580959df-bad0-f111-a05b-000d3a826b69`).
No genuine eligible Training request has been tested end to end.

The company clarified that Unpaid Leave is employee-chosen and does not require
exhausting paid leave. Its server guard requires a written reason and exact
saved weekday entries; an authorized Superior or HR decision creates no paid
balance debit. Future approved cancellation removes the public absence and
creates no ledger reversal. The signed assemblies and Superior review guidance
were deployed. The Leave Type readback shows one active Unpaid Leave record
(`9f14f001-bbd0-f111-a05b-000d3a826b69`). Local SDK safeguard checks pass;
no genuine eligible request has been tested. Replacement Leave was deferred by
the company and remains unavailable. Off in lieu remains on hold until HR sets
its earning and expiry rules.

## Emergency Leave Annual charging — Developer deployment, 25 October 2026

The company confirmed that an employee may submit Emergency Leave after the
absence with evidence; Superior approval charges the exact saved weekdays to
the Annual Leave balance. The local request guard now requires uploaded
evidence before approval, and synchronous Annual accounting uses the same
earned-cap and carry-forward allocation as ordinary Annual Leave. The balance
write guard verifies the Annual period and exact amount for an Emergency
request. Future cancellation uses the same exact-bucket restoration. Local SDK
tests pass for missing evidence, missing weekdays, one debit, retry rejection,
and March carry-forward-first allocation. The signed approval and balance
assemblies, HR correction permissions and cancellation form were deployed to
the Developer environment later on 25 October. The active Emergency Leave type
was created with hourly dates enabled. Genuine signed-in integration testing
is still outstanding.

## Compassionate Leave safeguards and employee form — 25 October 2026

The Developer environment now has an active Compassionate Leave type. The
published Employee self service form shows an editable relationship field only
when this type is selected; the published player displayed the field and its
eligible-relationship guidance in an unsaved draft. The draft was cancelled.
No live leave request was created for testing.

The signed server guard requires an eligible close relationship and three
consecutive full calendar days at submission. Approval requires the exact
saved full-day rows and an uploaded supporting document. The Superior or
authorized HR must inspect the evidence; the file-presence check does not
prove its validity. Approved future cancellation removes the public calendar
absence without inventing a balance reversal. The schema and read-only
Superior-review field are published; the signed assemblies were deployed and
read back. Two new local guard regression checks pass. Genuine eligible
submission, approval, email, calendar and cancellation checks remain open.

Training, Unpaid, Replacement and Off in lieu remain unavailable
until their category-specific evidence, decision authority, date and
accounting safeguards are complete. Planned Annual Leave remains requestable
after 15 December, per the company's later decision; this does not change
the 15 December written higher-carry-forward-limit deadline.

## Special-leave approval and cancellation safeguards — 25 October 2026

The signed Developer request guard now checks that Marriage, Maternity and
Paternity approvals have exactly one saved eight-hour date entry for every
required consecutive calendar day (3, 98 and 7 days respectively). A missing,
duplicate, inactive or partial-day entry blocks approval. The local SDK suite
passed and the deployed assembly's read-back hash was verified.

The published Superior review form now gives category-specific reminders to
inspect Sick/Hospitalisation evidence and the Marriage, Maternity or Paternity
eligibility details that cannot be proved by dates or an uploaded-file flag.
It explicitly says a separate HR verification stamp is not required for
Maternity or Paternity. The old optional HR-verification controls are hidden
on the published review form so they do not imply a second approval step;
their historical columns remain untouched. Its JavaScript checks and 18
existing form checks passed.

The Developer future-cancellation safeguard now accepts approved, wholly
future Marriage, Maternity and Paternity requests. An authorized decision
changes the original request to Cancelled in the guarded transaction, which
removes its shared-calendar absence. These categories do not have an
Annual/Sick/Hospitalisation balance debit, so the cancellation writes no
invented ledger reversal. The signed assemblies built and were deployed.
No genuine eligible special-leave approval or cancellation was used to test
this live path; these remain release checks. Older sections below describe
the earlier implementation and should be read as historical notes.

## Maternity and Paternity approval policy correction — 24 October 2026

The supplied specification names the Superior as approver for Maternity and
Paternity Leave. It does not require separate HR verification before that
decision. The local signed request guard has been changed to remove only the
mandatory HR-verification stamp; required notice/birth dates, consecutive-day
length, service and confinement checks remain. Local regression tests pass.
The corrected signed assembly was deployed to the Developer environment on
24 October 2026. Its read-back hash was verified, and the existing Create and
Update guards remain active. No real Maternity or Paternity request was approved
during this deployment. Older sections below describe the previous HR gate and
should be read as historical implementation notes.

Updated 23 October 2026. This records verified Power Apps / Dataverse changes.
The separate Next.js/PostgreSQL prototype is not credited here. Claims are excluded.
The user confirmed the records are dummy and asked to stop backup work; no further
backups were made after that instruction.

The latest fictional HR approval, exact-once Annual Leave deduction, repeat-
approval rejection, and Jennie/Yong app-role access checks are recorded in
`APPROVAL_VERIFICATION_2026-10-19.md`. Older dated sections below describe the
state at that time; use the latest note for current verification limits.
The eligible real-employee Sick grant and repeat-run ledger audit is recorded
in the 19 October update to `SICK_LEAVE_STATUS_2026-10-18.md`.

## Live test-account rule — 23 October 2026

For subsequent Developer-environment leave tests, use Darwisyah's own account
in Firefox to exercise employee submission and edit flows. Use Muhammad
Amirul's account only for the Superior approval or rejection step; he is
Darwisyah's current Superior. Do not create fictional leave requests. Run an
actual category request only when Darwisyah is eligible and the supplied dates,
evidence and declaration are genuine. A visual form check under Amirul's
account is not a substitute for Darwisyah's employee-session verification.

## Marriage Leave safeguard and form — 23 October 2026

The Developer Leave Request table now records the official Marriage Registration
Date and First Legal Marriage Declaration. The published Employee self-service
main form displays both fields only after Marriage Leave is selected, locks
End Date to Start Date plus two calendar days, and recalculates it when Start
Date changes. This was verified in the published app on 23 October with an
unsaved draft; the draft was cancelled without submitting a request.

The signed synchronous request safeguard requires the declaration and
registration date on submission. Approval requires three consecutive full
calendar days after registration and before its twelve-month anniversary,
probation completed before leave starts, an uploaded marriage certificate,
and no other approved Marriage Leave for the employee. The approver must
verify the certificate and declaration. The schema, model-driven review fields,
Marriage Leave type, and signed safeguard are deployed in the Developer
environment. Local regression tests and release builds passed.

No real Marriage Leave request or final approval has been run. The category's
end-to-end approval and HR notification still need verification with a real,
eligible employee; do not create a fictional request for that check.

## Paternity Leave safeguard and form — 22 October 2026

The Developer Leave Request table now has date-only Expected Confinement,
Actual Birth and Paternity Notice fields, plus a server-stamped HR verification
time. The published Employee self-service main form shows the three date
fields only for Paternity Leave and calculates End Date as Start Date plus six
calendar days. This was inspected in the published app on 22 October; the
draft was cancelled without submitting a request.

The synchronous request safeguard now checks Paternity submissions have a
notice date and an expected or actual birth date. Before approval, it requires
the actual birth date to equal Start Date, seven consecutive calendar days,
eight hours per calendar day, at least twelve months since Date Joined, and
an HR verification stamp. HR verification is restricted to an authorized HR
member other than the employee, stamps the server time, and rechecks the same
eligibility fields. Employee edits clear that stamp. The approval check also
rejects a repeat approved birth date and a sixth recorded confinement. HR
must verify the marriage, notice circumstances, birth evidence and prior
confinement history; a date or attachment alone cannot establish those facts.
The Developer review form exposes the dates read-only and the HR verification
field for that review. The signed safeguard assembly and form were published,
and the local plugin regression suite passed.

No real Paternity request, HR verification or final approval has been run.
This category's end-to-end behavior and the Employee-only access boundary
remain release checks. Do not create a fictional request to fill that gap.

## HR cancellation and Sick edit safeguard — 19 October 2026

The signed request safeguard was deployed after closing a Sick Leave edit
loophole: a pending request cannot move its start date outside the 48-hour
submission window measured from its original creation time. Local server checks
cover both a rejected late edit and an allowed timely edit. The Developer
`Leave HR` role now has Leave Cancellation Read and Write, and the cancellation
server guard and published form allow a verified directly assigned HR reviewer
to decide a pending cancellation regardless of the employee's synced Superior.
The employee may never decide their own cancellation.

Jennie Cheong's assigned HR role was impersonated in a Developer-only
integration test on a fictional future Annual Leave request. HR approval wrote
one -8-hour debit. A pending cancellation left it unchanged. Jennie's HR
decision changed the original request to Cancelled and wrote exactly one
linked +8-hour reversal. Repeating the decision was rejected with no second
entry. Her own-session form access and ordinary-role denial still need testing.
No production role or app was changed.

HR confirmed the company document's intention for Hospitalisation's 60 days
to include Medical Leave used. Current official Malaysian materials describe
non-hospitalisation Sick Leave and 60 days of hospitalisation Sick Leave
separately; enforcing the company calculation as a hard approval cap may reduce
statutory leave. A pure balance calculation now keeps both figures: the
company's combined 60 days after Medical/Hospitalisation use, and the separate
60-day statutory Hospitalisation remainder after Hospitalisation use. Tests
cover the 14-day Medical example, depletion of the company figure while
statutory Hospitalisation remains, cancellation-style reversals, and invalid
usage. The published Developer HR Employee form now reads the current-year
Medical and Hospitalisation ledgers and shows the combined company figure after
both uses, clearly labelled as reporting rather than an approval limit. A
dedicated Hospitalisation grant action creates the separate 60-day calendar
balance and adds credit only once; HR can invoke it on the Employee Annual
balance tab. Hospitalisation approval now requires an uploaded certificate,
a verified Superior or HR decision, and the saved grant. Approval and its
exact ledger debit commit atomically; approved future cancellation restores
the linked debit exactly once. A live fictional Developer test passed: no
certificate rejected without debit; certificate plus approval posted one
-8-hour entry; repeated grant added zero; cancellation posted one +8-hour
reversal and a repeated decision was rejected. Other incomplete category
rules remain gated.

The six employee-request findings reported on 16 October, the Darwisyah
attachment audit, the Note role change, the prior-year Date Joined metadata
check, and the remaining verification are tracked in
`EMPLOYEE_REQUEST_FIXES_2026-10-16.md`. The Employee app's main-form create/edit
route and signed server date-generation safeguard were published/deployed on
17 October. Live synthetic Annual and Sick Friday–Monday create/edit tests
passed; only Friday and Monday were generated and one duration applied to both.
The app's current configured types are Annual, Sick, Hospitalisation,
Maternity and Paternity. Other leave-category eligibility, evidence and
accounting rules remain incomplete and are not implied by the form update.

## App access and non-Annual approval safeguard — 18 October 2026

Microsoft's app access checker reported Jennie Cheong could not open **HR leave
operations** because she lacked **Read privilege on App Module**, despite a valid
license. The Developer environment's `Leave HR` and `Leave Superior` roles now
grant that privilege; both role deployments verified the privilege and their
respective app associations. Dataverse inspection found one active, matching
Entra user each for Jennie Cheong and Yong Khor Lord, both directly assigned
the HR and Superior roles. Their own sign-in and app-opening retest remains
pending; the role readback alone does not establish that the error is resolved
for those sessions. Production app sharing and role assignment are separate.

On 19 October, Misha's app error exposed a second missing model-driven runtime
privilege: `prvReadEntity` in `RetrieveUserContext`. She is the active Dataverse
user for Darwisyah Fakhira and currently has direct Employee, HR, and Superior
roles. Both hand-built HR and Superior roles lacked Entity metadata Read because
they did not inherit Microsoft's App Opener runtime baseline. The exact
`prvReadEntity` privilege was added at its required organization level to both
roles and verified; no other App Opener privileges were copied. Her own browser
retest is still pending and may expose another missing baseline privilege.

The synchronous Leave Request guard now refuses **approval** of any configured
leave type other than Annual while those categories lack complete eligibility
and balance accounting. Rejection remains available. This prevents a Sick,
Hospitalisation, Maternity or Paternity request from becoming Approved without
a corresponding debit. The signed safeguard assembly was deployed to the
Developer environment and its active request Create/Update steps verified.
Local full-suite tests and the signed build passed, including a Sick approval
denial with no decision history and an allowed Sick rejection. A live ordinary-
role approval attempt remains unverified. This gate is temporary until each
category has its own complete rules and accounting; it is not a finished Sick
Leave workflow.

The same access audit found that Darwisyah Fakhira, the intended ordinary
employee test account, had direct `Leave HR` and `Leave Superior` assignments.
After matching her active Dataverse user to her Employee Entra identity, those
two assignments were removed in the Developer environment. Readback showed
only `Leave Employee (development test)` as her direct role. Her own-session
negative tests for HR/Superior app and API access are still required.

## Revised specification work in source — 15 October 2026

The user confirmed ordinary HR notification without acknowledgement, a separate
HR-reviewed carry-forward process (section 4.2.9), an HR approval override
regardless of Superior availability, and employee-organized calendar invitations
to `all@asteriskdigital.com.my` titled `<Name> on leave`. See
`REVISED_LEAVE_WORKFLOWS.md` for the exact design and remaining dependencies.

Server changes now recognize a directly assigned `Leave HR` role in both
the PreOperation decision and PostOperation annual accounting checks, keep the
self-approval block, and distinguish HR employment-input maintenance from IT
changes to Entra identity and synced Superior. A `hr-role-deploy` command
prepares the scoped role without assigning users. The carry-forward draft
calculator enforces a standard five-day limit or an HR-verified written higher
limit and keeps unused remainder separate. The local SDK suite and both signed
plug-in assemblies build. The `Leave HR` role and both assemblies were deployed
to the Developer environment with verified assembly hashes, and the updated
review form was published. No user has been assigned the HR role. Live ordinary
HR sign-in/approval verification is still pending because the two intended HR
members have no active Dataverse `systemuser` there. No carry-forward record
has been posted, and no calendar invitation is live.

The carry-forward source derives a year-end review preview from the validated
Annual Leave ledger, checks consecutive source/target calendar years, and
preserves the original calculation when HR adjusts the amount. An override
requires a reason and supporting-document reference and cannot exceed unused
hours or the verified standard/special limit. On 15 October, the Developer
solution gained the `Annual Leave Carry-Forward Review` table with a one-review-
per-source-period key definition, original/reviewed amounts, evidence reference, HR
identity/times and operation key. It contains no created reviews. The scoped
`Leave HR` role gained read-only access to this table; no user was assigned.
The Employee form's Annual balance web resource now shows a read-only
carry-forward section: it explains that the current year must close first and,
for a preceding closing year, reads its recorded unused balance and any HR
review status/amounts. The page does not forfeit unused leave. The HTML check and SDK deployment/role checks
passed. Ordinary-role access to this new section still needs sign-in testing.

On 16 October, the Developer solution gained a separate Annual Leave Carry-
Forward Credit table with a one-credit-per-source key definition and read-only
`Leave HR` access. Both the review and credit tables have active synchronous
fail-closed guards for direct and bulk writes; live rejected-write probes left
both tables empty. The Annual balance tab now reads a posted prior-year credit
if one exists. Source confirmation planning checks a full closing-ledger hash,
the original HR limit, use-by date and retry identity. A local carry-first
allocation check covers 31 March, requests spanning the deadline and earned-
balance limits. The standard HR confirmation action is described below. The
accounting path is active, but positive live carry-use and ordinary-role access
remain to be verified before production use.

On 16 October, a separate Carry-Forward Usage table was also published with
immutable intended approval-debit and cancellation-reversal fields, a unique
operation-key definition, and read-only `Leave HR` access. Synchronous guards
are active on direct and bulk writes to all three carry-forward tables. Live
probes confirmed direct and bulk Create attempts are rejected and all three
tables remain empty. The local usage-ledger planner validates the entire
history, allocates carry-forward before earned leave through 31 March, and
plans an exact one-time cancellation reversal. On 16 October the signed
approval and cancellation accounting assemblies were deployed. They now use
a posted credit before current-year earned leave for dates through 31 March,
record the exact split in the synchronous decision transaction, and reverse
the same split once on cancellation. Local plugin, simulated rollback and
write-guard checks pass. Live direct/bulk write probes were rejected after
deployment. No credit has been posted, so positive live carry-use and reversal
remain unverified. Special-limit review, expiry and notifications are still
inactive.

The Developer solution also now contains `cr2c9_PreviewAnnualCarryForward`,
a read-only Employee-bound API for directly assigned active `Leave HR` users.
It computes the standard five-day candidate from the most recently closed
calendar year's complete validated ledger and returns the separate unused
amount; it creates no review or credit. Local tests covered HR/non-HR access,
unsupported inputs and the 64-hour unused/40-hour carry/24-hour separate
example. A live non-HR call was denied and left all carry-forward tables empty.
No HR account is assigned in this environment, so a positive live HR call
remains pending. The Employee record's Annual balance web resource now shows
the verified proposal only after a successful HR API response. The page test
passed and the updated resource was published.

On 16 October, the Developer solution gained `cr2c9_ConfirmAnnualCarryForward`,
an Employee-bound standard-limit HR action. It accepts no caller amount or
year. During 1 January–31 March, it locks the Employee and consecutive source/
target Annual Leave periods, rereads and validates both complete ledgers,
records a confirmed HR review and posts one separate credit (up to 40 hours)
in a synchronous transaction. A zero unused balance records a zero-credit
review. Retries return the existing matching review/credit; conflicting
history fails. It refuses to post after current-year leave has already been
accounted, because that requires explicit carry-first reconciliation. A
posted review/credit locks later approval and cancellation accounting against
the closing-year period. The Annual balance page now displays the HR-only
confirmation button only in January–March after a verified proposal. The
special written-limit exception remains unavailable in this action.
Local SDK and UI checks covered the standard 64/40/24 split, retry, simulated
rollback, zero credit and timing. Live non-HR calls and direct/bulk record
writes were rejected, leaving all three carry-forward tables empty. It is
October, with no HR role member or completed 2026 closing year, so a live
positive confirmation, special-limit evidence path, reminder/notification,
and production rollout are still pending. No year-end credit has been posted.

On 15 October, the 06:35 Malaysia-time daily Annual Leave grant run succeeded
for all 29 employee actions. A separate read-only ledger audit found Muhammad
Amirul at 84 earned/granted hours, eight hours of approved use and 76 hours
available. His two grant entries total 84 hours and predate this run, so the
run added no duplicate credit. Three fictional fixture periods still contain
historical excess credits; the real employee period audited has none. This
audit does not substitute for the next scheduled-run follow-up or ordinary-role
testing.

## HR annual balance read-only summary — 14 October 2026

The Employee form's **Annual balance** tab now reads the current calendar
year's Annual Leave Balance Period and signed Balance Entries on load, without
invoking the Grant action. It separately displays granted hours, net approved
usage after cancellation reversals, and available hours, each converted to
8-hour days. **Refresh balance** rereads the ledger; the existing Grant button
remains a separate action. A missing period is reported as ungranted, not a
zero entitlement. Invalid or overdrawn entries show a review error instead
of a plausible-looking balance. The form explicitly says first-year grants
and proration remain on hold, and labels synthetic periods as test balances.

The HTML web resource alone was published in the Developer environment; no
balance record or form metadata was changed by this deployment. On Muhammad
Amirul's saved Employee record, reopening **Annual balance** displayed the
synthetic 2026 fixture as 8 hours granted, 8 hours used, and 0 hours
available, without pressing Grant. The Node read-only summary checks and
.NET deployment-tool build passed. Darwisyah's ordinary-account request
submission and the HR/Superior review of that request still await her test.

## Ordinary Employee profile-write boundary — 14 October 2026

The existing employment input safeguard and Darwisyah's scoped role were
checked directly through the Dataverse SDK. Impersonated writes to her own
Employee Level, Entra identity, Superior lookup and Email were all denied;
administrative readback found those fields unchanged. Added local guard checks
for direct identity and Superior update attempts; the full .NET server suite
and deployment-tool build pass. No plugin or role was changed for this test.
The 14 October first-year and earned-leave decisions are documented in
`LEAVE_POLICY.md`; current source changes and live holds appear below.

The Superior app has Employees, Leave Requests and Leave Cancellations
navigation; the stale legacy Leave Approvals page was removed. A dedicated
Leave Superior role was created in the Developer environment and associated
with this app. It grants Global Employee Read without Employee Write and has
no assigned users. The 15 October sharing and assigned-staff view deployment
is recorded below; a separate ordinary Superior sign-in is still needed for
positive and negative access tests.

## Superior assigned-staff access — 15 October 2026

Deployed a private `cr2c9_superioraccessgrant` provenance table and six
synchronous sharing plug-in steps in the Developer environment. Requests and
cancellations receive direct Read/Write shares to the current synced Superior;
request dates and decision history receive Read shares. A Superior lookup
update reconciles existing request trees, including assignment changes. The
plug-in refuses to take over a manual share and removes only a share it
recorded. The Leave Superior role grants all-Employee Read without Employee
Write, and scoped access to shared leave records; nobody has been assigned the
role yet.

Published **Assigned pending leave requests** and **Assigned pending
cancellations** views in the Superior app, replacing its generic Active
Request/Cancellation views. Both require pending status and a grant marker for
the signed-in user. The fictional fixture backfilled three requests and eight
record grants; an assigned pending request then appeared in the view (one row),
and repeat reconciliation changed no grant count. This fixture temporarily
replaces the maker's Entra ID while submitting, so it needs a post-restoration
reconciliation and does not prove ordinary-account automatic sharing. Before
role assignment or production, audit/backfill other existing records and test
current and reassigned manager access, denials, evidence and decisions with a
separate ordinary Superior account through both app and Dataverse API.

## Employee annual balance page — 13 October 2026

The Developer-environment **Employee self service** Canvas app now has a
**My leave balance** button on Welcome, an Annual Leave summary page, and a
**Back to home** button. The page shows the signed-in employee's current
calendar-year grant, net approved usage after reversals, and available balance
as days at 8 hours per day. It refreshes the balance tables on entry. The
synthetic 2026 cancellation fixture for Muhammad Amirul displayed 1.0 day
granted, 1.0 used and 0.0 available; this is test data, not the company rate.

The first formula used a `With`-scoped balance-period record in the ledger
filter and Studio flagged a delegation warning. The published revision looks
up the signed-in employee's period ID on screen entry and filters each direct
Dataverse `Sum` by that ID. Studio shows no formula or delegation warning, and
Preview retained the same 1.0/1.0/0.0 result with the nondelegable row limit
temporarily set to one. The limit was restored to 500 before saving and
republishing. This checks the current fixture and Studio's delegation analysis;
it does not replace an ordinary-account positive-balance test or a large-ledger
production test. The page does not alter ledger records or entitlement policy.

14 October ordinary-account check: Darwisyah Fakhira signed in to the
published Employee self service app and opened My leave balance. Her welcome
page showed the Employee menus, and the balance page said no annual period was
set up for this year. Read-only inspection of her Employee form found Date
Joined, Employment Type, and Employee Level blank; the form's entitlement
diagnostic explicitly reports those three as missing for Annual Leave. Under
the confirmed null-input rule, the grant skips her until HR supplies valid
employment details. No placeholder allowance or test grant was created.
This verifies app entry and the missing-period message with her account, not
the positive balance path or broader ordinary-role authorization.

## Uniform Annual Leave requests and Home navigation — 12 October 2026

The Employee self service app was saved and Studio reported **Publish successful** after these
changes. The Leave Requests form now presents a 4/8-hour dropdown in place of
the numeric input. A new request saves its range and selected duration without
opening per-date entry. The pending request action reads **Edit leave request**;
its edit view locks employee, type, reason, status and attachments, leaving
From Date, To Date and duration editable. The save action patches only those
three fields. All generated Home icons in this app now navigate directly to
the Welcome screen. App checker showed no formula issues. A published-app
ordinary-user request submission and editing tests remain outstanding.

The older per-date screen remains available for non-Annual leave categories,
whose calendar-day and evidence rules have not yet been migrated to this
uniform Annual Leave handler. Their New/Edit actions still route there.

The registered synchronous Dataverse plugin creates one date row per
Monday–Friday date in the Annual Leave range, all at 4 or 8 hours. It replaces
those rows within the request-update transaction when a pending request's
range or duration changes. Direct per-date edits to a uniform request are
rejected; approved requests remain immutable. The live Developer-environment
test created a Friday–Monday pending fixture and verified exactly two 4-hour
rows, changed the duration to 8 and verified both rows, then verified a direct
mixed-duration edit was denied. Public holidays are not automatically excluded.
Existing mixed-duration requests have not been converted; an employee edit is
needed to place such a pending request under the uniform rule.

## Employee date-entry Back navigation — 12 October 2026

The Employee self service date-entry screen now has a secondary **Back to
requests** button beside Save date and Finish request. It resets the unsaved
date form and returns to the Leave Requests screen without submitting a date.
Studio Preview confirmed that Back returned from LR-0008 to the request list;
reopening LR-0008 still showed zero saved dates. App checker reported zero
formula errors, and the Developer environment publication succeeded at
3:38:18 PM Malaysia time.

## Employee request-list privacy — 12 October 2026

The generated Employee self service Leave Requests gallery searched the full
Leave Requests table, so the developer's administrator sign-in could see
Darwisyah Fakhira's LR-0033 alongside his own requests. Its Items formula is
now `Search(Filter([@'Leave Requests'], 'Employee Name'.'Entra User ID' =
Text(User().EntraObjectId)), SearchInput3.Text, 'Leave Request Number', Reason)`.
Studio reported zero formula errors; its preview showed Muhammad Amirul's
requests and no LR-0033. The app was saved and the publication notification
reported success at 11:18:54 AM Malaysia time. A separate refreshed Studio
preview of that version showed seven matching requests; searching specifically
for Darwisyah's LR-0033 returned no items. This is a Canvas presentation
filter. Dataverse role-scoped ordinary-user access remains the security
boundary; Darwisyah's impersonated API call previously could not read a
foreign request. Her later published-app sign-in verified entry and the
missing-balance-period message, but not request-list privacy in her session.
The maker's System Administrator role still allows direct Dataverse access in
this Developer environment. The shared absence calendar remains on hold.

## Date-entry range prompt — 12 October 2026

The user's LR-0008 spans 9 October only, while the generated date picker
initially displayed 12 October (today); the server correctly rejected that
out-of-range date. In the Employee app, the unlocked Leave Date card's picker
now defaults to the selected request's Start Date and limits selectable dates
to that request's Start Date–End Date. The Save date action also checks for a
blank or out-of-range selection and shows a plain message before submitting;
the server safeguard is unchanged. In Studio Preview, reopening LR-0008 showed
9 October selected. App checker reported zero formula errors. No new leave date
was submitted during this UI check. The app was saved and published at
11:25:31 AM Malaysia time; a refreshed read-only Studio preview also opened
LR-0008 with 9 October selected.

## Employee app date entry and cancellation — 12 October 2026

The Employee self service Canvas app was saved and published in the Developer
environment. After saving a new Leave Request, the employee is taken to a
date-entry screen to add each actual date as Half Day (4 hours) or Full Day
(8 hours). The screen identifies the request and its date range, lists saved
dates, and requires at least one saved date before Finish request returns to
the request list. Pending requests have an Add leave dates action so entry can
be resumed. A fictional pending request LR-0034 with a 30 October 2026 full-day
entry was created through Preview; reopening that request showed the persisted
date, and Finish request returned to the list. An out-of-range attempt on a
different pending request was rejected by the server. Evidence:
`employee-date-entry-preview-2026-10-12.jpg`.

The request list now has a Request cancellation action for an approved future
request. It opens a reason form that creates a Pending Superior Leave
Cancellation through Dataverse. The screen explains that the original approval
and balance remain unchanged until the Superior decides. A blank or unselected
request is blocked in Preview with a clear message; no cancellation was created
by that test. App checker showed no formula errors at publication. Evidence:
`employee-cancellation-preview-2026-10-12.jpg`.

The Canvas flow still needs an ordinary employee sign-in test and a real
approved-future Annual request to verify cancellation submission end to end.
The date-entry screen currently adds dates but does not edit/delete them; the
request becomes Pending Superior on the first save, before date entry is
complete, so the approval-side completeness guard remains essential. The
employee UI does not yet show cancellation history or send email
notifications. A later update displays pending cancellation status on the
request detail, as recorded below. Production rollout remains pending.

Read-only follow-up on 12 October found no approved future Annual Leave request
for Muhammad Amirul: his five future Annual requests were Pending Superior.
His assigned Superior is Yong Khor Lord, but no Dataverse systemuser currently
matches Yong's Entra ID in the Developer environment. His Employee row also had
no annual balance period, so a genuine Superior approval test remains blocked.

For the user's own-account cancellation UI test, a deliberately synthetic
Developer-environment fixture was created with
`cancellation-own-test-fixture`: request
`db519a92-3fc9-4bca-a0f1-97e6543fcc73` for 16 November 2026, one full-day
Annual Leave date, and an isolated 8-hour test period
`381fe531-358f-4839-99a5-ef5620d2deb7` containing an 8-hour grant and
matching 8-hour debit. The request reason explicitly marks it as synthetic;
there is no human approval decision stamp or notification. Postflight verified
the request, period, entries, net-zero available balance, and own Employee
link. A refreshed Employee app Preview displayed the record as `LR-0035`,
`Approved`, with the `Request cancellation` action. No cancellation request was
submitted; the user can test that step. This fixture enables the employee
cancellation request screen only; it
does not validate the real Superior approval path or cancellation reversal.

14 October: after the user submitted a cancellation for LR-0035, the Employee
self service detail still showed only the original Approved status. The Canvas
detail now checks the linked Leave Cancellation for Pending Superior and shows
"Cancellation pending Superior" in view mode. Its Request cancellation action
is hidden while that pending record exists, preventing a duplicate attempt.
The underlying Leave Request remains Approved and its debit remains in place
until a Superior decides. Studio Preview showed the new wording for LR-0035
and the unchanged Pending Superior status for LR-0034; no cancellation decision
or balance entry was changed during this UI work.

## Submission baseline — 11 October 2026

The existing synchronous, calling-user Leave Request Create guard was updated
with baseline submission checks. It requires an active Leave Type and both
calendar dates, and rejects End Date before Start Date. Live SDK negative checks
created no request and did not change the employee row. A valid fictional own
request saved as Pending Superior (`e082862f-4ac5-f111-a05a-000d3a826b69`).
One earlier fictional pending request was created while testing a time-bearing
SDK value: Dataverse normalized the DateOnly input to midnight before the
plug-in received it. Both fixtures remain pending with no approval or balance
deduction. The live test now verifies normalized DateOnly storage instead of
expecting a time-bearing rejection from the plug-in. Complete date entries,
category eligibility, evidence and sufficient-balance checks at submission,
and the employee Apply Leave UX remain outstanding.

The published Employee self service Canvas app now hides Edit and Delete on
the Leave Requests detail header, matching the server's submitted-request
write and delete restrictions. The Welcome screen hides Attachments,
Employees, Leave Types, Leave Approvals and Timesheets. The refreshed player
shows Leave Requests and Expense Claims only. Both changes were checked in
the refreshed player; evidence:
`employee-requests-review-actions-hidden.jpg` and
`employee-menu-hidden-after-publish.jpg`. This is UI cleanup, not completion of
the Apply Leave or cancellation request screens. A scoped Dataverse SDK call
impersonating Darwisyah Fakhira then created/read one fictional own pending
request (`ab8f08a4-4cc5-f111-a05a-000d3a826b69`) and its 4-hour date
(`ac8f08a4-4cc5-f111-a05a-000d3a826b69`), without approval or balance change.
Her own app sign-in and attachment-write path still need testing.

For the eventual company rollout, use `PRODUCTION_ACCESS_CHECKLIST.md` before
importing or sharing apps. It records the intended HR members and the separate
Employee, Superior, HR and IT/admin access checks; none of those production
assignments have been made.

## Latest scope clarifications — 10 October 2026

The user identified the people listed in HR Notification Emails as intended HR
members. Membership source is now confirmed; tenant identity resolution and
Dataverse/server permission implementation are still pending. Recording this
choice did not assign roles or enable HR approval override. Admin identity remains
unspecified. Separate HR/Admin acknowledgement is removed: email notification
only. Workflow notifications are email only. Everyone may see employee absences
in a shared calendar, preferably in Teams; this is low priority and not deployed.
See the latest section of LEAVE_POLICY.md for privacy scope and open details.

Employees choose their actual leave dates; holiday-calendar configuration and
automatic holiday exclusion are removed from scope. Annual accounting must use
explicit 4/8-hour date entries and retain range/duplicate/duration checks.
IT alone provisions accounts; sync creates/links Employee profiles and HR edits
existing employment inputs. HR employee creation is removed from intended scope;
native New commands and creation permissions have not yet been changed.

The unused unmanaged **System administration** app
(`cr2c9_Systemadmini29197942`) remains On. Read-only maker inspection of its
**Show dependencies → Delete blocked by** tab reported no items. App retirement
is recommended in response to the user's request for an opinion; no app,
site map, table, flow or platform administrator permission was deleted.

## Explicitly held by the user

HR approval override remains inactive pending secure authority implementation;
Admin identity and group/access configuration are unresolved. Rewards from eight
service years onward, first/last-year proration and rounding, taken/in-progress
leave cancellation and absence delegation remain held. The configured HR email
addresses are intended members, but are not themselves a live permission grant.
Employee, Superior, HR and Admin remain separate roles.

## Deployed and verified

Environment: `https://orgf7668341.crm5.dynamics.com`.
Unmanaged solution: **Leave and claim hub** (`Leaveandclaimhub`).

### Employee welcome menu — published later on 9 October

Employee self service Canvas app `a1f9f752-5a55-44d0-8027-8546b13743d9`
now hides the three tiles circled by the user: Attachments, Employees and Leave
Types. Set `Visible = false` on their complete welcome tile containers
`Container1`, `Container1_1` and `Container1_3`; the layout reflows. Published
successfully and refreshed the running player to verify all three are absent.
The other four tiles remained at that stage. Evidence: `employee-menu-hidden-live.png`.
Follow-up verification reproduced the user's Studio mismatch: the player hid
the tiles while newly opened read-only and editable Studio drafts still had
Visible=true. Reapplied the three container settings in the current draft,
saved and republished successfully at 19:22 MY time. Exited with Back/Leave,
reopened Studio and verified all three tiles stayed hidden in the saved draft;
also refreshed the published player. Evidence: `employee-menu-hidden-studio.png`.
Exited the verification editor through Back/Leave to release its editing lock.
This changes navigation in the dedicated employee app; it does not establish
ordinary-role Dataverse access permissions.
On 11 October, the Leave Approvals and Timesheets welcome containers
(`Container1_4`, `Container1_6`) were also set to `Visible = false`, saved and
published. The refreshed player showed only Leave Requests and Expense Claims;
evidence: `employee-menu-hidden-after-publish.jpg`.

### Annual grants and HR configuration — 10 October

Current-year annual grants are now deployed through the secured Employee-bound
`cr2c9_GrantCalendarAnnualLeave` action in separate Leave.BalanceGuards assembly.
All fourteen balance write guards stay enabled, permitting only scoped nested
writes from this synchronous action. Direct/bulk balance edits are denied.
Full-year eligible cases use January–December and anniversary rate increases;
missing required HR inputs and held policies skip credits and preserve history.
No retroactive grants, deduction backfills, approvals or test emails were made.
The main Leave.Plugins DLL and its nine decision/date/history steps are unchanged.

Published HR Employee Annual balance tab and optional Leaving Date. The button
calls the server action and displays added/available hours/days. Live fictional
fixture verification produced112h initial grant, zero retry, preserved credits
when an HR input was cleared, then8h anniversary difference. Restored original
five-year inputs resulted in128h across three credits. Direct CRUD and bulk
probes were denied; no extra entries were created. All219 local server checks
pass, including32 grant and maintenance boundary checks. Evidence:
`annual-grant-hr-verified.png`. Live competing grants and ordinary-role checks
remain required. See `BALANCE_IMPLEMENTATION.md` and deployed manifest.

Four Employee input guards now require organization-wide maintenance privilege
for supplied HR policy and identity/Superior fields. This administrative boundary
does not assign HR roles or settle held group mapping.

Created Text environment variable **HR Notification Emails**,
`cr2c9_HRNotificationEmails`, and configured both user-supplied recipients.
The existing superior-approval notification flow now reads that parameter;
its temporary developer recipient was removed. Added it to Leaveandclaimhub.
The current value is excluded from solution export and should be set separately
in each target environment. Saved flow and Dataverse value were verified;
no notification was sent. See `HR_CONFIGURATION.md` and its screenshots.

Daily **Grant calendar annual leave balances** flow was originally On, included
in the solution, and scheduled at06:30 Malaysia time after the06:00 sync. The fixture
run returned zero new credit; the active-employee manual run also succeeded across27employees.
It calls the secured action sequentially and sends no emails. Current plan
pagination limit is5000; larger organizations require explicit paging.
Evidence: `annual-grant-flow-active-succeeded.png`.

**14 October accrual change in progress:** the user confirmed monthly earned
annual leave, joining month counted in full, cumulative fractions rounded up
to the next half-day, and no use of future unearned months. The old daily flow
was located live by its exact name and verified **Off** in the Developer
environment. The HR Annual balance web resource was republished with its
manual grant control disabled and a warning that recorded remaining hours can
include unearned leave. No ledger entries were changed. The **repository
source** now changes the secured grant target to cumulative earned hours and
makes the approval transaction calculate an earned cap from saved HR details
and the current Malaysia date. It refuses future-year usage, unresolved
employment policy and a debit above earned remaining even when a legacy
full-year credit exists. It credits only positive monthly deltas and preserves
legacy overcredits for audited review. February–October tests yield 84 hours,
and February–December yields 104 rounded hours. SDK simulations and the plugin
build pass. The revised `Leave.BalanceGuards` DLL was deployed to the Developer
environment through `approval-accounting-deploy`; its existing synchronous
approval step and all balance guards stayed enabled. A live direct grant call
for the fictional fixture returned `PendingAdjustment`, credited 0 hours and
preserved its previously recorded 128 hours. This confirms the new grant
target is active. A separate fictional employee with a 20 February 2026 join
date received 84 earned hours by October; repeating the grant credited zero,
leaving exactly one grant entry. A fictional 11-weekday, 88-hour request was
denied without a status, history, or ledger change. A separate 8-hour request
was approved with exactly one decision and one -8-hour balance entry. The HR
notification trigger excludes that fictional employee from email. This live
test exercises the recorded-balance boundary. A second live check then used
the authorized grant API to overcredit only that fictional fixture, restored
its February joining date, and tried the pending 88-hour approval again. The
ledger showed 108 hours available, yet the server rejected the approval with
"Insufficient earned annual leave balance" and left the request, history, and
ledger unchanged. This independently verifies the **earned cap**. Do not
reactivate the daily flow until real legacy-overcredit review and balance-
display changes are complete. The
employee balance page still needs to show earned available leave instead of
recorded remaining. The published HR web resource showed the warning and
disabled grant button.

### Annual approval accounting — 11 October

Annual Leave approval now invokes the enabled synchronous PostOperation
`Leave annual approval accounting` step in the separate Leave.BalanceGuards
assembly. It reads saved 4/8-hour date entries, validates the approved request,
decision history and current Superior, and deducts only those selected dates.
The debit, status, decision history and balance locks share one Dataverse
transaction. Missing or insufficient balances abort the approval. The existing
main Leave.Plugins DLL and its nine safeguards were not replaced.

Live SDK checks on a dedicated fictional employee verified: empty dates,
136 hours against 128 and an ungranted 2027 period each rolled back to Pending
without history or debit; a 4+8-hour approval changed availability 128→116
with one debit; repeat approval was denied; rejection wrote history without a
debit; a grant retry did not restore used leave. Two concurrent 80-hour
approvals against 116 hours produced exactly one approval and one debit,
leaving 36; retrying the other failed. A final exact 36-hour approval left zero.
Direct bulk ledger creation was denied. Bulk request UpdateMultiple was denied
by the existing identity safeguard before the accounting step. The fictional
employee is excluded by a verified trigger condition from the otherwise-On HR
approval email flow, so these tests did not notify HR. The published HR Annual
balance tab displayed zero remaining hours, and its guidance now describes
selected-date deductions. Local checks include a simulated nested SYSTEM caller
and foreign-actor rejection. Evidence: `annual-approval-balance-zero-live.png`. See
`annual-approval-accounting-2026-10-10.json` and `BALANCE_IMPLEMENTATION.md`.

The published Superior form was subsequently tested with fictional request
LR-0022. A developer account assigned as its synced Superior approved one
future eight-hour date. The saved request has one decision-history entry and
one −8h ledger debit; the fictional employee's available balance changed
128→120h. This verifies the published form path with an administrator account,
not ordinary-role Dataverse permissions. Company production access remains
deferred.

### Future approved-leave cancellation — development only

Created the Leave Cancellation table with an original-request lookup, reason,
pending/approved/rejected status, decision identity/time, comments, row-version
input and server lock. Synchronous Create/Update/Delete guards permit the
requesting employee to submit a future approved Annual Leave cancellation and
only the latest synced Superior to decide it. Pending requests retain the
original approval and debit. Approved cancellation writes an exact positive
reversal linked to the original debit and changes the original request to
Cancelled in one Dataverse transaction. Repeat decisions and second reversals
are denied; taken or in-progress leave remains outside scope.

A live fictional mixed 4+8h SDK fixture verified Pending left the Approved
request and zero remaining hours unchanged; the Superior decision changed
the original to Cancelled and restored exactly 12h once. Local server checks
cover mismatched, missing, duplicate and orphan reversals. This is a developer
account test, not ordinary-role access verification.

The cancellation main form has the review fields and a published JavaScript
library with current-Superior and row-version checks. The published Superior
app now shows Leave Cancellations in its navigation. A second fictional
approved request LR-0022 gained a pending cancellation; the original approval
and 120h available balance remained intact. The developer account decided it
through the published Superior form. The saved record became read-only, the
original request became Cancelled, and a separate SDK check confirmed exactly
one +8h reversal linked to the −8h debit, restoring availability 120→128h.
This verifies the form with an administrator account. The Employee app
cancellation screen was added later, as described in the 12 October section.
Cancellation emails are not implemented.

Access inspection found no Dataverse `systemuser` for either configured HR
email address, and the signed-in developer is a System Administrator. Darwisyah
Fakhira has since been prepared as an ordinary Employee test user in the
Developer environment: Environment Maker was removed, a scoped development
Employee role assigned, her own Employee row transferred to her, and Employee
self service shared as User. Impersonated Dataverse reads passed own Employee
and shared Leave Type access and denied foreign Employee, Leave Request, saved
date, balance period, ledger entry, decision and Note attachment records. An
effective-rights check denied maintenance rights on her own Employee profile.
Her own published-app request path, indirect/team roles, attachment write paths
and attachment write paths still need checking. A different ordinary
Superior account is also needed for the real-role decision path. The concrete
test matrix is in `ACCESS_VERIFICATION.md`.

### Employee profile

Added optional profile inputs (initially unpopulated), with form fill assistance disabled:

- Position text (`cr2c9_position`, maximum 100 characters).
- Employment Type (`cr2c9_employmenttype`): Full time 290040000,
  Part time 290040001, Intern 290040002; no default.
- Probation Completed On (`cr2c9_probationcompletedon`): DateOnly behavior/format.
- Employee Level (`cr2c9_employeelevel`), no default: Operations / Analyst /
  Senior Analyst 290040000; Consultant / Senior Consultant 290040001;
  Sales / Lead Consultant (Manager) 290040002; Principal Consultant
  (Senior Manager) / Director 290040003. Documented base allowances are
  14/14/15/18 days; no entitlement calculation or level assignment is deployed.

Published Employee Information form `ae8d487a-1898-43fd-b9ac-b7b980d2628c`
with those fields and read-only **Superior (latest sync)**. Verified against
the developer's own profile in the published HR runtime. No values assigned.

Latest user correction after inspecting the sync: HR must maintain missing
employment details, restoring HR profile-input maintenance to scope and
superseding the sync-only instruction. Directory identity and Superior remain
sync-maintained; HR-maintained employment inputs must survive daily sync. If a
required calculation input is null, skip calculation and mark it pending HR
details, without inventing defaults or zeroing existing balances. This changes
the intended implementation; HR-only field permissions and null-input handling
have not yet been deployed. HR identity mapping remains held.

Read-only entitlement input inspection on 9 October found no daily-sync mappings
for Position, Date Joined, Employment Type, Employee Level, or Probation Completed
On. Aggregate counts show all four new inputs remain empty; six legacy Date Joined
values cannot be credited to this sync. Existing-employee update actions have no
profile fields mapped. See `ENTITLEMENT_INPUTS.md` for inspected mappings and the
next source/rule decisions. At that inspection no calculations were deployed.
Subsequent rate-preview deployment is described below; sync remains unchanged.

### Entitlement rate preview — subsequently deployed

Employee-bound function `cr2c9_GetEntitlementPreview`
(`870324e7-c2c3-f111-a05a-000d3a826b69`) uses
`Leave.Plugins.EmployeeEntitlementPreview` to read saved Date Joined, Employee
Level and Employment Type as the initiating caller. No SYSTEM reads, writes,
grants or balance operations. Additional custom processing steps are disallowed.
The signed assembly was updated; the existing nine guard steps are retained.

The user selected service-anniversary increases. Assessment uses today's Malaysia
calendar date (UTC+8) and completed service years. For confirmed full-time cases
below eight years, annual full-year rate is max(level base + service reward,
statutory minimum): base 14/14/15/18, reward 0/1/2/3, minimum 8/12/16. Annual hours
are days × 8. Full-time sick rate is 14/18/22; separate hospitalisation is 60.
This does not implement proration, period grants, accrual, or balance credits.
Rates recalculate when queried; no anniversary balance-credit job exists.

Null required inputs return `PendingHrDetails` with null days, never zero/defaults.
Annual needs joining date, level and employment type; medical rates do not need
level. Optional position/phone/probation fields do not block these previews.
Invalid/future dates and unknown choices are flagged. Eight-plus-year annual
rewards, part-time/intern rules and 29 February anniversary handling produce
`PendingPolicy` with null affected rates, pending confirmation/review.

Employee form loads `EmployeeEntitlement.onLoad` from
`cr2c9_employeeEntitlement.js` (`cc0324e7-c2c3-f111-a05a-000d3a826b69`). It displays
rates or pending details; unsaved changes invalidate the display; save/data
refresh recalculates. Errors/timeouts show unavailable without writes. Legacy
Annual Leave Entitlement Days is hidden/read-only in this form, retaining its data.
Superior remains read-only. HR-only field permissions and sync identity guards
remain pending; these UI protections do not establish data-layer authorization.

Verification: 31 calculation cases, 3 API access/target cases and 9 new form cases
pass, alongside prior 51 server guards and 16 review-form cases. Plug-in and
deployment-tool builds succeeded with zero warnings/errors. Live SDK checks
confirmed missing inputs skip, fictional five-year rates 16/22/60 and null level
skips annual independently. Preview calls leave Employee row versions unchanged.
Only fictional fixture `a3a2b544-41de-4385-bacf-9859c45656c5` received test inputs;
no real synced employee received policy defaults. Published HR runtime verified
clear level + save → pending HR; restore + save → Annual 16 days. Evidence:
`entitlement-null-input-live.png`, `entitlement-calculated-live.png`.

### Calendar dates

Changed existing Start Date, End Date and Date Joined from UserLocal to
**DateOnly behavior and DateOnly format**. Dependency preflight found forms
and views, with no workflow dependencies or inspected cloud-flow date references.
Converted existing UTC values with Microsoft's `ConvertDateAndTimeBehavior`
message, SpecificTimeZone, Kuala Lumpur / Singapore time-zone code **215**.
Job `65b0ab4c-b3c3-f111-a05a-000d3a826b69` completed successfully (status 30).
Verified all 9 populated Start Dates, 9 End Dates, and 6 Date Joined values
against their original UTC+8 calendar dates, held in memory during conversion.
LR-0006 now displays **7 October 2026** for both dates in the published HR app.
Decision On remains an actual UserLocal timestamp; user timezone display
settings have not been standardized to MYT.

### Per-date duration and documents

User/team-owned `cr2c9_leaverequestdate` has required parent lookup, DateOnly
Leave Date and Duration choice: Half Day (4 hours) 290040000, Full Day
(8 hours) 290040001, no default. Composite request/date key
`cr2c9_uniquerequestdate` is Active; parent relationship Delete is Restrict.
Published its Information form and Active Dates view
`3204bdc4-db74-476f-a5de-e89770d96b20` (ascending Leave Date).

The review form includes related **Leave dates and durations**, with editing
and navigation disabled in its grid component. Native New commands remain;
server guards now deny date edits on decided/historical requests and deny
editing another employee's pending date entries. Pending own entries accept
only the 4/8-hour choices and require a date and parent. Entries must fall within the saved Start/End Date range, and date writes also
lock and recheck the employee identity row. Schedule, holiday and category
validation remain outstanding. Existing requests were
not automatically expanded into child dates.

Supporting Document (`cr2c9_supportingdocument`) is a real optional File column,
maximum 32768 KB, read-only on the review form. Historical Attachment text is
retained. Employee upload UX, required certificates and file-message API guards
remain unfinished; do not claim full attachment immutability.

### Server safeguards

Deployed signed sandbox assembly **Leave.Plugins**, database storage, with
three plug-in classes and nine **synchronous PreOperation (stage 20)** steps:
Create/Update/Delete on Leave Request, Leave Request Date and Leave Decision.
Steps run as Calling User, rank 10, without filtering attributes, and are
included in the existing solution. Exact step names are part of the trusted
internal-call contract: `Leave safeguard: <logical table> <message>`.

- New requests require explicit Pending Superior and the caller's active,
  verified Employee identity. Client decision stamps are rejected on Create.
- Review updates allow only a transition from Pending Superior to Approved or
  Rejected, plus comments and stamps. Saved details, historical status and
  repeat decisions cannot be overwritten. Request deletion is blocked.
- The server verifies the actual caller, active requestor and assigned Superior,
  comparing Entra object IDs and rejecting self-approval. Authority follows the
  latest successful Employee sync, not a live Microsoft Entra query.
- Hidden `cr2c9_decisionexpectedversion` is the review version input. Compare-and-
  swap row-version locks serialize changes to the request and identity rows.
  Missing/stale input fails; the transport value is removed before persistence.
- Decision identity/name/time are overwritten with server-verified values and
  the UTC server clock. History creation occurs in the same database transaction.
- New user/team-owned `cr2c9_leavedecision` contains parent request, outcome,
  actor ID/name, timestamp/comments and operation key. Restrictive parent
  relationship and Active unique operation key `cr2c9_uniquedecisionoperation`.
  History can be created only by the registered server decision step; all
  updates/deletes are denied. Pre-existing decision stamps/history retained;
  no synthetic trusted history was backfilled.
- Hidden lock token columns on Employee/Request support transactional locks.
  Internal requests verify server-owned parent step context and linked request.
  Dataverse's intervening ObjectModel context and injected modification/timezone
  columns were observed and accounted for during live testing.

Published version-aware `cr2c9_leaveRequestForm.js` and hidden expected-version
control on Information form `f297bf4c-1a1d-4560-b594-572612a10d5c`; preserved
OnLoad/OnSave registrations with execution context. Async OnSave is published
in both Superior and HR apps. HR runtime URL is now verified and working.

### Decision history screen

Published related **Decision history** subgrid `cr2c9_decisionhistory_review`
on the existing review Information form. View **Active Leave Decisions**
`fd907846-da63-4861-b3dc-5403cf41c662` shows Outcome, Decided On,
Decision Maker Name and Decision Comments, newest first. Technical operation
names/keys are excluded from the view. No view switching/search, five rows.
Both HR and Superior apps include Leave Decision and Leave Request Date as
non-navigation table dependencies. Explicit Power Apps grid controls disable
editing, navigation and multi-select on Web, Mobile and Tablet; chart switching
is disabled. Published customizations and verified the fictional LR-0009 row
in both running apps. Native New commands remain; server guards deny forged
history and updates/deletion. View-only command cleanup is still needed.

### Verification

**16 form mocks and 51 server checks passed.** Server mocks reproduce observed
system columns and intervening core contexts. Real SDK integration checks:
repeat approval, overwriting a decided request, forged history and adding a
date to approved leave all denied. Failed date transaction retained original
status, decision timestamp and row version. Missing/stale decision version
inputs denied. A fictional LR-0009 was rejected by its assigned Superior;
server replaced forged stamps, created exactly one linked history record,
removed the transport version and denied history edits/deletion/repeat decision.
The fixture is clearly labelled, uses `example.invalid`, and remains for review.
No approval notification was triggered by these rejection tests. Live own-employee
fixture LR-0010 accepted mixed 4/8-hour dates, updated a duration, rejected an
out-of-range date with parent-version rollback, and deleted a pending date.
One full-day entry remains for review; status remains Pending Superior. The
running form blocks self-review and shows the retained date.

This earlier section covers the original decision safeguards. The later annual
accounting checks above verify live competing decisions, and LR-0022 verifies
approval through the published Superior form with the developer account.
The limited Darwisyah read-access checks above do not verify ordinary-role
approval behavior or constitute production security certification.

Hashes, step IDs, conversion job and test fixture references are recorded in
`deployment-2026-10-09.json`. Source: `server/Leave.Plugins/LeaveGuards.cs`.
Evidence: `employee-profile-published-hr.jpg`, `leave-dates-corrected-live.jpg`,
`hr-async-save-enabled.jpg`, `decision-history-live.jpg`,
`decision-history-superior-live.jpg`, `own-pending-date-live.jpg`, and prior
column/key screenshots.

## Still required

10 October: both balance keys are Active and scoped annual grant protection is
live as described above. HR and Superior review forms show Canvas Note attachments;
LR-0007’s file was verified and downloaded. Ordinary-role published-app access
and wider write paths remain unverified; Darwisyah's SDK own-request and date
create path passed as described above.
See ATTACHMENTS_STATUS.md. The user reported the Canvas network banner had stopped
and closed that investigation; no cause was claimed.

Employee Apply Leave completion and ordinary-role access; HR employment-input maintenance,
ordinary-role protection of sync-owned identity/Superior fields;
held entitlement cases and category validation; employee cancellation
end-to-end verification, status/history UI and notification;
shared absence calendar; email notifications and delivery retry/deduplication;
remaining leave categories; file API safeguards; distinct-role tests.
Role-specific administration and account provisioning are not established by
these guards. The employee Canvas welcome menu was subsequently modified as
described above; the HR notification recipient is now configured as described above; sync is unchanged.
Organization auditing is still OFF despite table/column audit configuration.
No broad HR override or privileged role mapping was enabled. HR membership source is now identified by the latest clarification above.
# Earned annual credit cleanup — 14 October 2026

The daily **Grant calendar annual leave balances** flow is On again in the Developer environment. Its bound grant action now credits only the increase in cumulative annual leave earned through the current Malaysia month, including the joining month, rounded up to the next half-day. The HR annual-balance page is read-only and no longer offers the old manual full-year grant button. The employee entitlement notification no longer describes proration as pending.

A read-only audit of all four 2026 Annual Leave periods found **no excess credit in the one non-test employee period** (Muhammad Amirul: 8 hours granted, 8 used, 0 recorded remaining, 84-hour earned cap on 14 October). The other three periods are explicitly fictional development fixtures with `@example.invalid` addresses: their historical credits exceed the new cap by 32, 20, and 20 hours. One fixture also has 8 hours of historical usage above its cap. These entries remain as audit history; they are excluded from production data migration. The server approval path already caps spendable hours at the newly earned amount, even when an older ledger contains more credit. Do not manually delete or rewrite protected ledger entries to hide the fixture discrepancy. The local audit export is ignored by Git.

The flow was checked as the sole cloud flow with the expected bound annual-grant API and Employee target before activation; the post-change state verified as On. No immediate flow run or grant was initiated. Recheck run history and the next day's real employee ledger increment after the scheduled execution.

At 22:28 Malaysia time on 14 October, the Power Automate flow details page independently showed **On**. The latest run was the successful 06:29 run on 14 October, before the new grant path was enabled that evening. The first scheduled run under the new path was therefore still pending at that point; a past successful run did not verify the new behavior. Darwisyah's Date Joined, Employment Type and Employee Level remained blank, so her missing balance period was expected until HR entered verified values. At that point, the ordinary Superior access test was unavailable because the intended Superior had no active matching Dataverse user; the subsequent sharing deployment is recorded above.

A thread follow-up named **Verify annual leave grant** was scheduled after the daily flow's morning run to check the first post-change run and ledger without sending routine unchanged updates. It was paused after the 15 October verification below.

## Superior restructuring sync — 21 October 2026

The Developer-environment **Sync Employees from Microsoft 365** flow still looks up each employee's current Microsoft 365 manager and updates the Employee table's Superior lookup. A synchronous server step then reassigns the recorded Superior shares on the employee's existing leave requests, request dates, decision history, and cancellations in the same transaction. The former Superior loses the shares created by this process; the new Superior receives them. If the new Superior has no unique active Dataverse account, the update is rejected and the previous assignment remains in place. This prevents a partial handover.

The flow previously could finish successfully while an employee's manager mapping was missing or a row update failed inside a condition. It now records those per-employee failures, runs its manager loop sequentially, and fails the whole run with the affected Microsoft 365 user IDs and Dataverse error messages. A 404 from the Microsoft 365 manager lookup remains the intended path for a person with no manager and clears the old Superior. Backups of the flow definition were written to the private temporary directory before each update.

Two manually started live runs tested the change. The final [run on 21 October](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/flows/8f9df3ab-3093-9122-497d-479fc8f52b5a/runs/08584090266751000512647516040CU21?v3=false) correctly failed and named four employees whose assigned manager has **no matching Dataverse user**: Akram Hilmi, Chrislyn Jules, and Lee Shan Yue report to Looi Ming Chean; Liow Yee Shuen reports to Toh Xin Ying. Those two managers need an active Dataverse account and the appropriate app role before their employee assignments can complete. No account or role was provisioned in this change.

A read-only audit checked 70 requests, 255 dates, 14 decision histories, 5 cancellations, and 276 share markers. The real employee requests checked had the expected current-Superior shares. The 97 audit findings were limited to the four missing manager accounts and older explicitly fictional development records with missing share markers or Superior-owned rows. No new fictional request was created for this verification. The historical fixture records remain untouched; a future manager change involving one of those fixtures may still need separate cleanup. Darwisyah's existing requests remain assigned to Muhammad Amirul, with the old Superior removed from the managed shares.

## Live earned-credit flow test — 14 October 2026, 22:34–22:35 Malaysia time

A new read-only `annual-grant-preflight` command assessed every active Employee using the same server earned-cap calculation before changing data. Of 29 active profiles, four were calculable (one real and three fictional) and 25 had incomplete employment details. The only positive grant difference was Muhammad Amirul's: 84 hours earned minus 8 hours previously granted = **76 hours**. All fictional fixtures were already overcredited and had zero additional credit due. The preflight made no writes.

The **Grant calendar annual leave balances** flow was then run manually in the Developer environment. [First run](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/solutions/~preferred/flows/61002ed5-7150-750a-b966-a4708e51c6fb/runs/08584096184294503484189115816CU21) succeeded. Ledger readback showed the expected single new 76-hour grant: 84 hours total granted, 8 hours used, 76 hours remaining, three entries, and no excess above the 84-hour earned cap. A [second manual run](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/solutions/~preferred/flows/61002ed5-7150-750a-b966-a4708e51c6fb/runs/08584096183676078905618192721CU28) also succeeded; a final ledger audit still showed 84 granted and three entries, so the repeat run added **zero** hours. No new fictional balance credit appeared. The flow remains On for its next scheduled run. Darwisyah still needs verified HR inputs before a positive ordinary-account balance test.

## First scheduled earned-credit run — 15 October 2026

The [06:30 Malaysia-time scheduled run](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/flows/61002ed5-7150-750a-b966-a4708e51c6fb/runs/08584095898854667404213735711CU28) succeeded in 14 seconds. Read-only audits before and after that run show the real 2026 period unchanged at **84 hours granted, 8 used, 76 remaining, three entries**, matching the 84-hour cumulative earned cap. The 15 October preflight calculates zero further credit for the real employee and all three fictional fixtures; their prior historical overcredits remain unchanged. The 25 incomplete active profiles remain held. This verifies that this scheduled repeat added no duplicate credit. The one-time thread follow-up was paused; the daily Power Automate flow remains On.

## Daily Hospitalisation grant — 25 October 2026

The Developer-environment **Grant calendar annual leave balances** flow now invokes `cr2c9_GrantCalendarHospitalisationLeave` for each listed active employee after its existing Annual and Sick grant actions. The action targets the same Employee row and uses the server's idempotent, eligibility-checked 480-hour calendar-year grant. The flow checker reported zero errors and warnings after saving. The flow details page subsequently showed **On** and **Scheduled**.

The [manual live run](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/flows/61002ed5-7150-750a-b966-a4708e51c6fb/runs/08584087283116937821970066970CU03) succeeded in 37.5 seconds. Before the run, Muhammad Amirul's published employee dashboard reported no 2026 Hospitalisation period. After refreshing it, the dashboard showed **60.0 days granted, 0.0 used, 60.0 remaining**. The HR app showed one `Hospitalisation Leave 2026` period (`f2bf31db-f4cf-f111-a05b-000d3a826b69`) and exactly one `hospitalisation-grant:480` ledger entry (`f3bf31db-f4cf-f111-a05b-000d3a826b69`). Darwisyah's pre-existing 60-day Hospitalisation balance was already backed by one grant entry before this run; her published dashboard showed 60.0 days granted and remaining after the run. No fictional leave request was created.

This verifies automated provisioning and dashboard readback for the two checked real employees. A genuine eligible Hospitalisation request with a certificate is still needed to verify live approval deduction and subsequent cancellation reversal through the employee and Superior apps. The dashboard's combined Medical/Hospitalisation company figure remains reporting only; Hospitalisation approval retains the separate 60-day entitlement under `LEAVE_POLICY.md`.

## Emergency Leave and HR correction — Developer deployment, 25 October 2026

The company confirmed that Emergency Leave may be submitted after the leave with evidence and must charge Annual Leave on approval. Local server changes validate evidence and saved weekday rows and debit the same Annual/carry-forward buckets used by Annual Leave. The company then authorized HR to reverse an erroneously approved past Emergency Leave with a recorded reason. The local cancellation guard permits only verified HR to create or decide that past-date correction, bars self-correction, and uses the existing exact-once reversal of the saved Annual/carry-forward split. The HR cancellation form now allows creation of a reasoned pending correction before separate review and decision.

These changes passed 133 local SDK safeguard checks, 38 balance-persistence checks, 40 approval-transaction checks and release builds. The signed Leave.Plugins and Leave.BalanceGuards assemblies, HR cancellation role permissions and cancellation form JavaScript were deployed to the Developer environment. Form metadata readback shows the published cancellation form's load/save handlers. The active `Emergency Leave` type (`596ac321-76d0-f111-a05b-000d3a826b69`) was created with `Is Hourly Based = Yes` and an explanation that approved hours deduct Annual Leave. A read-only type inspection found exactly one active Emergency Leave record. A genuine eligible request, approval debit, email/calendar update, and reasoned HR correction with balance restoration have **not** been tested live. Corrections after the closing year's confirmed carry-forward are still blocked by the existing ledger guard so a posted closing balance cannot be silently changed; that case needs a separate reconciliation design. No fictional live requests were created.
