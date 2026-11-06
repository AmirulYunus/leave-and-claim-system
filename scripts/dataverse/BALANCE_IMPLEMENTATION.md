# Balance implementation — development in progress

Updated 11 October 2026. Live environment `https://orgf7668341.crm5.dynamics.com`,
solution `Leaveandclaimhub`. No record backups, retrospective credits or
historical deduction backfills were made.

## Current request

The employee Canvas app now hides the Attachments, Employees, Leave Types,
Leave Approvals and Timesheets welcome tiles. Published and verified in its
running player. This is a menu change in the dedicated employee app, not
Dataverse access enforcement.

The user also requested yearly grants and approved-leave deductions. On
10 October the user chose calendar-year annual balances, 1 January–31 December.
The earlier anniversary decision still governs service-based **rates**.
First/last-year proration and rounding remain explicitly held. A new calendar
year does not by itself expire, reset or carry forward earlier balances.
The user deferred company production-environment access while development continues.

## Live current-year grant action — 10 October

The separate signed **Leave.BalanceGuards** assembly now exposes Employee-bound action `cr2c9_GrantCalendarAnnualLeave` and calls the tested calendar assessment and persistence adapter. The existing main Leave.Plugins assembly was not uploaded. The action takes only a saved Employee; Malaysia server date and saved HR inputs determine the year and allowance. It ignores the model-driven Web API app-name telemetry parameter, never using it for authorization. Client amounts, years, categories and ownership are rejected.

The action requires `prvCreatecr2c9_leavebalanceperiod` and organization-wide Create depth, an enabled initiating user, and caller read access to the Employee. No role/group grants or HR approval override were added. Optional DateOnly Leaving Date is now available for HR maintenance. Active record state and active employment are checked. Missing required inputs skip grants and preserve history; first/last-year, eight-year, leap-day and unsupported-employment policy cases remain held.

Employee and period CAS locks, overlap checks, Active keys and a synchronous transaction protect writes. Annual Leave is explicitly bound to development reference `959c93de-1197-4de4-a660-2e2c3266f0fe` and must be configured/rebound to the migrated reference before production activation. Medical/calendar-day categories cannot enter this action.

All fourteen original balance steps remain enabled. They now permit only fixed-scope writes nested under the stage-30 transactional grant action for the same initiating user. Append-only positive Grant entries inherit period ownership. Direct CRUD, Assign, SetState and bulk ledger writes remain denied, including administrators. Dataverse merged CreateMultiple/UpdateMultiple pipeline contexts for single SDK writes are supported only inside the trusted action and only for one row. No client flag or SharedVariables bypass exists; the API permits no additional processing steps.

Four Employee input safeguard steps require organization-wide Employee maintenance privilege for joining date, level, employment type, active/leaving/probation/position and directory identity/Superior fields. This is an administrative boundary, not an HR identity mapping. Daily sync uses its existing privileged owner; identity/Superior ownership and ordinary-role rollout remain unverified.

The published HR Employee form has an **Annual balance** tab and a Grant button. It refuses unsaved HR details, calls the action, and displays added/available hours and days. Missing balance is represented by API AvailableHours=-1 and shown as not calculated, never as zero entitlement. The UI was verified on the fictional employee: AlreadyGranted, added zero, available128 hours/16days.

Live SDK checks verified first credit112h, retry0h, null-input preservation112h, anniversary delta8h, leaving-date hold and direct write rejection. Restoring the fixture’s original five-year details added8h, giving three immutable credits totalling128h. Regression confirmed retries add nothing, external bulk writes fail, and null inputs preserve128h. The first test’s late assertion assumed the user profile lacked HR inputs; it actually returned PendingPolicy for first-year employment and received no grant. No own inputs were changed; corrected regression passed. No requests were approved and no emails were sent. All219 local server checks pass, including32 grant/maintenance boundary checks. Live competing writers and ordinary-role grants remain unverified.

`annual-grants-2026-10-10.json` records the deployed action and assembly hash. `annual-grant-regression` is rerunnable; `annual-grant-test` expects a fresh fixture without a period. Historical staging commands now refuse use after grant API activation.

## Daily development grant flow

**Grant calendar annual leave balances**, resource
`61002ed5-7150-750a-b966-a4708e51c6fb`, is On and included in
Leaveandclaimhub. Recurrence: daily06:30 Singapore Standard Time (MalaysiaUTC+8),
starting11October2026, following the unchanged06:00 directory sync. It lists
active Employee IDs (`statecode eq 0 and cr2c9_isactive eq true`) in stable order
and sequentially calls the bound grant action. It has no email action.

The fictional-only manual run succeeded and returned AlreadyGranted0h,
available128h. After removing the fixture filter, the actual active-employee
manual test also succeeded across27employees. The action independently validates eligibility;
missing/held inputs do not create zero-balance periods. Connector retries
cannot double-credit grants. Current environment plan accepts pagination
threshold5000 and rejects100000; the saved job uses5000. Larger-than5000
employee populations require explicit paging before rollout; this development
organization is below that limit. Production connection ownership and licensing
still need the separate rollout review.

Evidence: `annual-grant-flow-fixture-succeeded.png`,
`annual-grant-flow-active-succeeded.png`.

## Accounting core and approval adapter


`server/Leave.Plugins/BalanceLedger.cs` plans annual-hour ledger entries using
explicit employee/type/period identities and supplied dates. It does not write
Dataverse, contain a registered plug-in, choose a balance year or authorize
approval. `server/Leave.Plugins.Tests/BalanceLedgerChecks.cs` verifies the
accounting independently of the live system.

- A policy-approved full-period grant credits the difference between the target
  total entitlement and all grants already credited. Reruns do not replace the
  available balance or restore used leave. An increased allowance can add only
  its difference. A lower rate is pending adjustment and preserves the grant.
- Pending HR details, pending policy, invalid inputs and uncalculated rates
  produce no credit and preserve existing ledger history and availability.
- Approved annual leave uses individual 4/8-hour dates and splits across the
  explicitly supplied periods. Empty/duplicate dates, uncovered dates,
  overlapping periods, foreign employee/type/history and insufficient balances
  fail before any plan can be persisted.
- An exact approval retry produces no second debit. A different amount for the
  same request conflicts. The immutable request/date guards must remain in
  place; these accounting entries retain amounts, not a second date history.
- Ledger validation rejects duplicate operations or request deductions,
  overdrawn history, orphan/duplicate reversals and reversals with a different
  amount. At this original planning checkpoint reversal validation was
  preparatory; the later cancellation deployment is recorded below.

34 accounting checks passed, including mixed half/full days, consumed balance
preservation, anniversary deltas, cross-period insufficiency without a partial
plan, and null-input handling. These are in-memory checks, not evidence of
live transaction atomicity, access enforcement or concurrency.

## Calendar grant assessment and transaction adapter — 10 October

`CalendarAnnualGrant.cs` supplies explicit current-year boundaries and the
current anniversary-based annual rate. Missing required HR inputs preserve
pending status. Inactive/prior-departed employees receive no new grant, and
first/last-year, eight-year reward, leap-day and unsupported employment cases
remain pending. Leaving date is optional for an ongoing employee; employment
input ownership and target schema mapping must be enforced by the future caller.
21 checks pass, including Malaysia midnight/year rollover and anniversary changes.

`BalancePersistence.cs` is an internal SDK adapter, not an IPlugin or exposed API.
It requires a synchronous transaction, verifies each stored period against the
provided scope, locks periods in stable GUID order with IfRowVersionMatches,
then reads all ledger pages before planning writes. It rejects incomplete or
corrupt history and copies period ownership to entries. All cross-period
amounts are validated before the first Create. SDK faults propagate without
retry; the future registered caller's transaction must roll back status,
history and ledger together. It does not use ExecuteTransaction/ExecuteMultiple.

29 SDK simulation checks pass for repeated grants/approvals, missing HR,
anniversary deltas, cross-year insufficiency, CAS conflicts, paging, corrupt
history, ownership and rollback of a later write fault. Combined assessment/
storage checks verify that held first-year grants preserve consumed balances
and new-year grants leave the previous period untouched. This is simulated
rollback, not evidence of live Dataverse transactions or ordinary-role access.
The adapter is called by the deployed grant action and annual approval step.
The user removed holiday-calendar automation from scope; employees select their
actual leave dates.

## Live annual approval deduction — 11 October

`AnnualApprovalAccounting.cs` registers a synchronous stage-40 Update step on
Leave Request, filtering Status and requiring a pre-image. The existing stage-20
review guard validates the assigned Superior and writes the status, server stamps
and unique decision history. The accounting step verifies that saved decision,
all explicit child dates, active Annual Leave type and matching calendar-year
periods, then uses `BalancePersistence` to lock the periods and append negative
Approved Leave entries. The balance guards accept those exact nested writes
only in this server transaction. The original request and balance records roll
back together on any failure. There is no client-supplied amount or bypass flag.

Live fictional-employee checks: 128h granted; mixed half/full days deducted12h;
insufficient136h, empty dates and ungranted2027 dates rejected without a decision;
grant retry left116h; competing80h decisions allowed one, leaving36h; an exact36h
approval left0h. The rejected request gained history but no debit. Repeat
decision and external bulk debit were denied. Bulk UpdateMultiple of a request
was denied by the existing identity guard before reaching balance accounting.
The HR email flow remains On with a condition excluding this dedicated fictional
employee; other employees continue to use the configured recipients. No test
approval emails were sent. The main Leave.Plugins DLL remained unchanged.
Local server suite: 251 checks, including32 approval/accounting cases. Live
ordinary-role checks, grant
versus approval contention remain open.

## Dataverse persistence schema

Deployment tool commands `balance-inspect` and `balance-schema` inspect or
prepare the following tables without creating employee balances:

| Table | Contents | Uniqueness |
| --- | --- | --- |
| `cr2c9_leavebalanceperiod` | Employee, Leave Type, explicit DateOnly Period Start/End, server lock token | Employee + Leave Type + Period Start |
| `cr2c9_leavebalanceentry` | Period, optional Request, kind, signed integer Hours, operation key, optional Reverses Entry | Operation Key |

Tables are user/team-owned. Lookups use restrictive deletion. Ledger kinds are
Grant 290040000, Approved Leave 290040001 and Reversal 290040002; hours use an
integer range of -100000 to 100000. At the schema-only checkpoint there were
no balance records, defaults, role assignments, jobs or grant APIs. Keys had to
be Active before use, and processing waited for server write guards. The live
grant and approval accounting layers are described above.

Created and published both tables in the live development solution on 9 October.
Period table ID `89abe7ec-cac3-f111-a05a-000d3a826b69`; entry table ID
`e0952e0a-cbc3-f111-a05a-000d3a826b69`. Optimistic concurrency is enabled
on both. The initial post-deployment inspection reported both key builds
Pending. A fresh SDK inspection on 10 October confirmed both keys **Active**.

## Historical staging protection — superseded by grant activation

Deployed the separate signed Leave.BalanceGuards assembly to keep the prepared
tables closed while the authorized balance writer is unfinished. Fourteen
synchronous PreOperation steps cover Create, Update, Delete, Assign, SetState,
CreateMultiple and UpdateMultiple on both tables. Every write is rejected;
there is no role, depth, parent-context or caller-input bypass. This is staging
protection, not the final authorized-writer/append-only implementation. It was replaced by the scoped transactional guards described above.

Verified all fourteen step registrations and four live SDK probes: direct and
bulk Create against each table were rejected by this plug-in. Both tables
remained empty. The ledger probes omit a period reference because an invented
reference fails platform validation before the plug-in; they test rejection,
not acceptance of a valid grant. No live Update/Delete against existing balances
was possible because no balance records exist. Registration IDs and the separate
assembly hash are in balance-protection-2026-10-10.json. The existing live
Leave.Plugins assembly was not replaced; grants/debits were inactive at that staging checkpoint.

## Still required

1. Before production, rebind the explicit Annual Leave reference, configure the
   target notification Current Value and connections, and verify ordinary HR,
   employee and administrator permissions. Do not infer authority from emails.
2. Build the employee date-entry and balance UI around the verified explicit
   4/8-hour records; do not infer hours from an inclusive range.
3. Verify cross-period approval with both years granted and competing grants
   against approvals. Insufficiency rollback and competing approvals passed
   live checks on the fictional fixture.
4. Verify ordinary-role access. Published Superior approval and cancellation
   form decisions passed with an administrator account. Employee balance
   display and cancellation request UI remain unfinished.
5. Resolve explicitly held proration/rounding, eight-year rewards and leap-day
   service cases before enabling them. Carry-forward and expiry remain unconfirmed.

**Current-year annual grants, approved Annual Leave deductions and exact future-cancellation reversals are active in development.** Fictional SDK and published-form cancellations restored 12h and 8h respectively, each exactly once. Ordinary-role access remains unverified.
