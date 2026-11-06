# Revised leave workflows (15 October 2026)

This records the Developer solution plan and deployment progress. The revised leave specification's section 4.2.9 and its diagram govern year-end carry-forward; claim management is outside this work.

## Ordinary decisions

An assigned Superior or a directly assigned `Leave HR` Dataverse-role member may decide a pending leave request. HR is not conditional on the Superior being away. The caller's active Entra identity is checked, the requestor cannot decide their own request, and a single decision/history/accounting transaction must remain atomic. The employee and HR receive email notifications. There is no HR acknowledgement step. Pending cancellation still uses the existing Superior decision path until its separate authorization and UI are updated; HR receives a notification after the decision.

The server checks for the HR approval override are deployed in both the request decision and annual accounting stages in the Developer environment. The scoped `Leave HR` role is associated with the HR app and has no assigned users. The review form web resource now recognizes HR for the controls while the server remains authoritative. Ordinary HR-account sign-in, HR app sharing, positive/negative API tests, and a real approved-leave decision are still required. `cr2c9_HRNotificationEmails` routes email only and does not authorize approvals.

## Carry-forward, section 4.2.9

1. Remind employees on 1 October and 30 November. A request to carry more than five days needs written immediate-Superior approval submitted to HR by 15 December. HR verifies and records the approved special limit and its evidence.
2. After the 15 December planned-leave cut-off, let existing requests finish review through 30 December. At year-end, snapshot the final approved usage and unused Annual Leave in the closing calendar-year balance.
3. Calculate the normal candidate as the lesser of unused leave and five days (40 hours), or the lesser of unused leave and an HR-verified written special limit. Track the remainder separately as unused leave; do not silently erase it or book it as carry-forward.
4. Show a draft summary to HR, permit a controlled adjustment with a reason and audit trail, and require explicit HR confirmation. Only then post one carry-forward credit to the next year's separate carry-forward bucket. Use an operation key tied to employee, source period and target period so retries cannot credit twice.
5. Lock the prior-year snapshot, notify the employee, show carried-forward and ordinary annual balances separately, and remind about the 31 March use-by date. Expiry/forfeiture postings remain disabled until the statutory and company treatment is validated.
6. For a January–March Annual Leave approval, deduct from the posted carry-forward credit first and only then from the current year's earned balance. A request spanning 31 March may use carry-forward only for its dates through 31 March. Record the exact split against the request so cancellation restores the same two buckets once. Do not treat a carry-forward credit as a current-year earned grant.

The `CarryForwardReview.Prepare` core derives unused hours from the validated
closing-year ledger and requires consecutive calendar-year periods. Its
confirmation plan checks the complete source-ledger snapshot and duplicate
credit identity. The deployed approval and cancellation accounting assemblies
use a confirmed separate credit first for eligible January–March dates and
reverse the exact saved split once. The Developer solution has review, credit
and usage tables with active direct/bulk write guards. The Employee form's
Annual balance tab calls the HR-only read-only
`cr2c9_PreviewAnnualCarryForward` API. During January–March it can show a
**Confirm standard carry-forward** button to a direct `Leave HR` role member
after a verified preview. The deployed `cr2c9_ConfirmAnnualCarryForward`
action then rechecks and locks both years, saves the confirmed review and
separate standard-limit credit in one transaction, and refuses to post after
current-year leave has already been accounted. The prior-year ledger is
locked against later approval/cancellation accounting after posting.
Local tests cover the 40-hour standard cap, separate unused amount, repeat
confirmation, rollback, zero credit and 31 March cutoff. Live non-HR and
direct/bulk writes were rejected. No carry-forward record has been posted,
so positive live confirmation, carry use and reversal remain unverified.
Lawful expiry handling is still pending. Existing
annual-grant automation is separate from carry-forward processing.

On 23 October, the Developer solution gained an immutable **Written Carry-Forward
Exception** table and an HR-only action. During 1 October–15 December, HR can
record a written immediate-Superior approval reference and a limit above five
days, in half-day steps. The server verifies the current Superior identity,
HR role, source Annual Leave period, deadline and unique source-year key.
Preview and confirmation recheck that saved exception; the updated HR Annual
balance tab exposes the action. No exception or credit was posted during
deployment. A separate active flow sends the employee and HR an email only
when a positive carry-forward credit is created. Another active flow checks
daily at 06:30 Malaysia time and sends employee reminders only on 1 October
and 30 November; its first possible send is 30 November 2026. Live flow
definitions and activation were checked after deployment. A real 2026
closing-year HR confirmation cannot be exercised until January 2027. HR can
also reduce the proposed credit in half-day steps, with a mandatory reason
and supporting document reference. The original calculation, reviewed amount,
reason and document reference are stored on the immutable review; server
confirmation and the write guard both recheck them. This cannot increase
carry-forward above verified unused leave or the approved limit. The
adjustment contract and HR screen were published on 23 October; no balance
was changed.

On 23 October, a server safeguard was added for January–March Annual
Leave approvals: when the preceding year's recorded Annual Leave balance has
unused hours but no confirmed carry-forward credit, approval now stops with an
HR-confirmation message instead of charging current-year earnings first. Local
transaction tests cover that case, a fully used prior year, and leave after
31 March. The updated signed assembly was deployed to the Developer environment
through the existing HR confirmation deployment path. A live non-HR confirmation
was denied and left review, credit and usage storage unchanged. A positive
January–March approval cannot be run yet because the 2026 source year has not
closed; no test leave request was created.

### Carry-forward test with Darwisyah's actual balance

The read-only Developer command `carryforward-darwisyah-dry-run` validates
Darwisyah's complete current-year Annual Leave ledger, applies any saved
written special limit, calculates the proposed credit and separately tracked
unused balance, and counts existing HR reviews and credits. It creates no
request, review, credit, or ledger entry. On 23 October 2026, her eight ledger
entries yielded 32 unused hours (four days). With the standard 40-hour limit
and no written exception, the hypothetical year-end credit was 32 hours and
the separately tracked remainder was zero. There were no existing HR reviews
or carry-forward credits. This is a snapshot, not a final 2026 amount.

On 3 November 2026, the HR Employee Annual balance tab gained a **Preview this
year's carry-forward** button. It estimates the current year's candidate from
the recorded Annual Leave balance and any saved written higher limit, labels
the result provisional, and makes no writes. Clicking confirmation before the
source year closes explains that 2026 carry-forward cannot be confirmed before
1 January 2027; the server's independent year-end gate remains in force. A
read-only rehearsal of Darwisyah's 2026 ledger on 3 November found 8 unused
hours (one day), a 40-hour standard ceiling, no written exception, and no
existing review or credit. This is an estimate, not a posted balance.

After 31 December, use Darwisyah's actual closing balance for the live test:
refresh the HR preview; verify its unused amount against the ledger and the
five-day or verified-exception cap; have HR confirm once; verify one review and
one separate credit, the employee notification, and unchanged current-year
Annual Leave grants. Repeat confirmation to verify it cannot credit twice.
For an actual January–March request made by Darwisyah, check that approval
spends the carried-forward bucket first and that cancellation restores the
saved split exactly once. Confirm that dates after 31 March cannot spend the
carry-forward bucket. Do not create fictional live requests solely for this
test. Legal/policy validation still gates any expiry or forfeiture posting.

On 15 October, the 06:35 Malaysia-time annual-grant flow run reported success across 29 employee actions. A read-only ledger audit found Muhammad Amirul had 84 earned/granted hours, eight approved hours used, and 76 hours remaining, with no excess credit or duplicate operation key. The run added no further grant; his last grant was recorded on 14 October. Three fictional test periods still contain excess historical grants (32, 20 and 20 hours respectively), and one fictional period has eight hours of usage beyond its currently earned cap. Those test balances must not be used as evidence that every balance is clean. The next scheduled run remains due for its separate follow-up audit.

## Organization leave calendar

The published Developer employee app now has a month-view calendar backed by the
organization-owned `cr2c9_publicabsence` table. The synchronous approval guard
publishes a row containing only employee display name, start/end dates and
4/8-hour daily duration. Approved cancellation removes that row in the same
transaction. A dedicated `Leave Calendar Reader` role grants organization-wide
Read on this table only; the deployment command assigned it to the five active
employees whose Dataverse users match their Employee Entra IDs on 23 October.
On 23 October, the active Microsoft 365 Employee sync was updated to write
each person's Entra ID on both existing-record paths and new Employee records.
A synchronous Employee Create/Update step now reconciles that identity to a
unique enabled Dataverse user and assigns only the dedicated calendar reader
role to an active linked employee. It removes that role when the Employee is
deactivated or unlinked. If the Dataverse user account does not exist yet,
the next scheduled sync retries the assignment. A controlled update of
Darwisyah's real Employee record restored the role after it was temporarily
removed, and all five currently linked active employees were verified to
have calendar reader access. No leave request was created for this test.
Earlier approved requests
were not bulk-published because the Developer database contains development
fixtures that must not appear as real absences. The calendar begins with new
approvals after this deployment.
The published view was opened with Darwisyah's account on 23 October; the
October and November month views and navigation loaded. The projection table
still had zero rows, so appearance of a newly approved real leave and removal
after an approved cancellation remain unverified in the employee UI. No
fictional leave request was created for this check.

On 23 October, Darwisyah's signed-in employee app showed LR-0098 as Approved
for 22–23 October, while the maker portal showed no rows at all in Public Leave
Absence. This is an older approval that was not copied into the projection when
the calendar launched; the empty calendar does not mean her approval was lost.
After authenticated Dataverse access became available, the guarded
`public-absence-lr0098-inspect` command confirmed status Approved, Darwisyah,
22–23 October, eight hours per day, and no projection. The targeted
`public-absence-lr0098-backfill` command added a projection with the request ID
as its ID so an approved cancellation can remove it. The public absence audit
passed with one approved row, and Darwisyah's published October calendar showed
her name and “Full day” on both 22 and 23 October. No fictional test request was
created.

The user subsequently tested an approved cancellation and reported that the
notification emails arrived and the employee calendar reflected the change.
The user also confirmed that Annual Leave approvals and cancellations have
calculated the balance correctly in their testing. This is a user-reported
functional check; the request ID and before/after ledger figures for the
cancellation test were not supplied for an independent accounting audit.

**23 October implementation choice:** Build a read-only calendar in Employee
self service first. It reads a dedicated public absence projection containing
only approved request identity, employee display name, date range, and
full-/half-day label. The projection must be written by the server on approval
and removed on approved cancellation, with no employee write privilege. Ordinary
employees must not gain organization-wide Read on private Leave Requests. No
leave type, reason, evidence, balance, or decision data belongs in the public
projection. The invitation plan below is retained as a deferred alternative;
do not activate it alongside the in-app calendar by default.

On the first final approval, an asynchronous flow should create one calendar invitation from the **approved employee's Microsoft 365 mailbox**, addressed to `all@asteriskdigital.com.my` with subject `<Name> on leave`. Use an all-day Malaysia-time event from the approved first date at midnight to the day *after* the approved last date at midnight. The public event must contain no leave category, reason, evidence, balance, decision notes or attachment. Mark invitees free and suppress response requests so the organization calendar is visible without blocking everyone's availability. This is a calendar invitation, with no Teams call link: colleagues need to see absence, not join a meeting.

Store the recipient in a solution environment variable such as `cr2c9_AllStaffCalendarRecipients`, with `all@asteriskdigital.com.my` as its production current value. The actual event ID and delivery state belong to each leave request, not the environment variable. A 4-hour leave request should be labeled as a half-day in the public event; the exact AM/PM placement remains undecided, so it must not imply a morning or afternoon absence.

Creating an event in each employee mailbox from one background process requires IT-administered Microsoft Graph `Calendars.ReadWrite` application access, scoped to the relevant mailboxes. A fixed Power Automate Outlook connection would instead send from that connection's account. Do not activate the flow until IT grants and scopes this access. Use `/users/{employeeEntraId}/calendar/events` and a stable `transactionId` derived from the leave request ID; persist the returned event ID and invitation state in Dataverse. On retries, reconcile the stored event before another create. When an approved leave is cancelled, cancel/update that same event and record the outcome. Surface failures for retry, without rolling back the already-final leave approval. If `all@asteriskdigital.com.my` is a distribution list, IT must verify it expands to the intended staff and that mailbox/calendar limits permit delivery.

The calendar flow has **not** been created or activated, and no organization-wide invitation has been sent. The employee's email/calendar ID must resolve from the verified linked Microsoft 365 user, not a free-text Employee field.
