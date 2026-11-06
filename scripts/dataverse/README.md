# Power Apps Leave development

These scripts implement the live Dataverse solution, separately from the Next.js
prototype. Current verified deployment is tracked in `IMPLEMENTATION_STATUS.md`
and `deployment-2026-10-09.json`. No further backups are being performed following
the user's instruction that all records are dummy.

Daily sync mappings and entitlement input gaps are recorded in
`ENTITLEMENT_INPUTS.md`. Confirm authoritative source fields before implementing
entitlement calculations; legacy dummy values are not policy inputs. The latest
user correction restores HR maintenance of employment inputs missing from sync;
skip calculations with null required inputs until HR completes them.

## Employee entitlement rates

Employee form `ae8d487a-1898-43fd-b9ac-b7b980d2628c` loads
`cr2c9_employeeEntitlement.js` with `EmployeeEntitlement.onLoad` and execution
context. Its Employee-bound function `cr2c9_GetEntitlementPreview` calls
`server/Leave.Plugins/EntitlementPreview.cs` as the initiating user. It reads saved
HR inputs only; incomplete inputs produce pending HR/null rates. These are
full-year rates, not grants or available balances. Assessment uses the current
Malaysia date with anniversary changes. See `IMPLEMENTATION_STATUS.md` for
supported/held cases and live HR save verification.

Run `node --test scripts/dataverse/employeeEntitlement.test.cjs` for form checks.
The server test executable includes calculation/access checks. Development-tool
commands `entitlement-inspect`, `entitlement-deploy`, `entitlement-test` inspect,
deploy and test; `entitlement-test` changes only the fictional Employee fixture.
The legacy annual-days field is hidden/read-only on the published profile form.
Administrative Employee input guards are now deployed; HR identity/group mapping and ordinary-role verification remain pending.

## Review form

Published web resource: `cr2c9_leaveRequestForm.js`.
Information form: `f297bf4c-1a1d-4560-b594-572612a10d5c`.
Keep the enabled `LeaveRequestForm.onLoad` and `LeaveRequestForm.onSave` handlers
with execution context, once each. Async OnSave is enabled and published in both
Superior review workspace and HR leave operations.

The review form also contains linked date entries and a Decision history grid.
Include Leave Request Date and Leave Decision as non-navigation table dependencies
and save/publish each model-driven app after changing its dependencies.

The form needs Employee Name, custom Status, Decision Comments, the three
Decision By Entra ID / Decision By Name / Decision On stamps, and
`cr2c9_decisionexpectedversion`. Entra ID and Expected Version controls are hidden
and read-only. Decision On is an actual timestamp with UserLocal display.
Start Date, End Date and Date Joined now use DateOnly behavior and format.

Only a verified assigned Superior can change an existing Pending Superior
request to Approved/Rejected with comments. Other requests remain read-only.
The form compares the request's saved version on load and before save, then
submits the expected version. Missing identities, stale status/version, changed
assignment, self-approval, failed reads and an eight-second timeout block saving.
Authority follows the latest successful Employee sync, not a live Entra lookup.

## Dataverse server guards

`server/Leave.Plugins/LeaveGuards.cs` is deployed in signed sandbox assembly
**Leave.Plugins**, stored in the Dataverse database. Nine synchronous stage-20
steps cover Create, Update and Delete on `cr2c9_leaverequest`,
`cr2c9_leaverequestdate`, and `cr2c9_leavedecision`. They are in Leaveandclaimhub,
rank 10, Calling User, with no filtering attributes. Their exact names must remain
`Leave safeguard: <logical table> <message>`: internal calls verify the registered
server step envelope through the ParentContext chain, not client-supplied flags.

The server checks active Employee and Superior Entra identities and no self-
approval, restricts pending-to-final transitions, rejects detail overwrites and
repeat decisions, and sets verified decision stamps using the UTC server clock.
Expected Version is transport only; it is removed before persistence. Row-version
compare-and-swap locks on the request and identity rows protect the operation.
History creation is in the same transaction, with a unique per-request operation
key. Direct history Create and all history Update/Delete are denied. Failed
operations roll back status, stamps, locks and history. Existing legacy statuses
and old decision stamps are retained; trusted history is not backfilled.

Per-date guards require a linked request, date and exactly the 4/8-hour choices,
prevent reparenting and deny changes to decided/historical requests. Only the
request's own active verified Employee can change pending dates. Request deletion
is denied. Native grid New commands may remain visible; server rejection enforces
these rules. The shared review form is not the employee submission screen.

These are decision safeguards, not a complete leave engine. The live Create
guard now requires an active Leave Type, Start Date and End Date, and rejects
reversed dates. Category eligibility, complete per-date entries at submission,
supporting evidence, available-balance checks before submission, balances for other categories/reversals,
cancellation, HR employment-input permissions and protection of sync-owned fields,
null-input calculation handling, file-upload messages and
ordinary-role record access still need implementation/verification. HR override
and role/group mapping remain held; Admin-specific maintenance authorization is
not established by these guards. Organization auditing remains disabled.

## Development and verification

Build the signed plug-in with a .NET SDK. Its local signing key is ignored and
must be retained for compatible future assembly updates. It targets net462 and
uses Microsoft.CrmSdk.CoreAssemblies 9.0.2.60; no extra runtime dependencies.

- Form checks: `node --test scripts/dataverse/leaveRequestForm.test.cjs` (16 passed).
- Server checks: `dotnet run --project scripts/dataverse/server/Leave.Plugins.Tests -c Release`
  (including 18 submission checks). The fake SDK reproduces observed system audit/timezone columns and
  the intervening ObjectModel context; it is not a live concurrency test.
- SDK deployment helper: `tools/Leave.DataverseTool`, .NET 10. `develop` signs in
  using Microsoft's documented public device-code client, keeps tokens in memory,
  verifies the fixed environment/development identity, then accepts explicit
  commands. `schema`, `publish`, `deploy` are mutations; `inspect`,
  `date-preflight` are reads. `fixture` is a pre-activation fictional test seed.
  `smoke` and `positive` are integration tests; `positive` decides the fixture once.
  `dates` changes metadata and submits a KL UTC+8 conversion job; use
  `verify-dates` in the SAME process to compare in-memory expectations.
  Do not rerun a conversion blindly or enable deployment before its key is Active.
  The older `backup` command is retained as an unused recovery tool, not run.

Live SDK tests on 9 October rejected repeat approval, decided edits, forged
history, approved-request date changes and stale/missing versions. A fictional
**LR-0009** was rejected by its assigned Superior; the server replaced deliberately
forged stamps, wrote exactly one linked history, removed the transport version,
and blocked history edits/deletion/repeat rejection. Rejected date writes left
LR-0006's status, decision timestamp and row version unchanged. No approval email
was triggered by these rejection tests. Separate-account access, live racing
writers, new approval through the hardened form and positive employee date
editing remain integration work.

## Existing flows

**Sync Employees from Microsoft 365**
(`0b6f0a80-cac2-f111-a05a-000d3a826b69`) remains enabled, daily. It
populates Employee Superior from Entra manager data. On 23 October 2026 its
identity-match, email-match and new-row branches were updated to save the
Microsoft 365 Entra object ID on the Employee row. Email-matched existing
employees can therefore gain the immutable identity needed for calendar access.
The active Employee Create/Update calendar-access plug-in reconciles the
dedicated `Leave Calendar Reader` role for a unique, enabled Dataverse user
linked to an active Employee. The daily sync retries this reconciliation for
existing staff; employee deactivation or an identity change removes obsolete
membership. Darwisyah's own active Employee update was tested by temporarily
removing her calendar role: the plug-in restored it, without creating a leave
request. The role has global Read on only the public absence projection among
leave tables; Dataverse retains its standard SharePoint-data baseline privileges
even after a privilege replacement. The plug-in rejects any other privilege.

On 21 October 2026, a changed Entra manager exposed a reassignment failure:
an older leave-decision history row was owned by the former Superior, so
Dataverse would not revoke that user's access. The server now transfers such
history ownership to the leave request owner within the same transaction,
then moves only the plug-in's recorded shares to the new Superior. New
decision history starts with the request owner to avoid this case. The history
guard permits the owner transfer only from the verified employee-manager
sync step; ordinary history edits remain blocked. The deployed assembly was
verified by SHA-256 after upload, and server tests cover reassignment.

The live sync subsequently changed Darwisyah Fakhira's Employee Superior from
Yong Khor Lord to Muhammad Amirul and moved her request access accordingly.
In Amirul's Superior app, list-page rejection of Darwisyah's marked test
request LR-0092 succeeded; her published employee app showed **Rejected**
after refresh. The flow's overall success banner can still mask a failed
individual employee iteration, so inspect the per-employee actions when
investigating future sync discrepancies.

**Notify HR and employee after leave decision**
(`9646d8f4-96c3-f111-a05a-000d3a826b69`) is active. It watches committed
Approved or Rejected transitions on Leave Request, regardless of leave type,
with a verified decision identity and time. The existing HR recipient parameter
(`cr2c9_HRNotificationEmails`) still routes HR mail through the authorized
Outlook connection; the employee message uses the email on the linked Employee
record. Existing fictional Employee exclusions remain on this flow.

**Notify HR and employee after leave cancellation decision**
(`a7c6c81b-9ace-f111-a059-000d3a826b69`) is also active. It watches final
Approved or Rejected transitions on Leave Cancellation, resolves the original
request and its Employee, then sends each recipient the result. It skips the
three prior fictional fixture employees. Both flows fail visibly if a linked
Employee record is missing. No historical decision was replayed or notification
sent during deployment. Delivery and retry behavior still need validation on a
real future decision; do not create fictional leave requests for that test.
HR acknowledgement is out of scope: notification only.

References:
- [Dataverse plug-in registration](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)
- [Optimistic concurrency](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optimistic-concurrency)
- [Date behavior and existing value conversion](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/behavior-format-date-time-attribute)
- [Async OnSave](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/events/form-onsave)

The old simulation file is retained; never upload it as the active resource.

Date entries now require a valid saved request range and fall within its boundaries.
The `own-dates` development command verifies live mixed-duration create, update,
range rejection/transaction rollback and deletion on a clearly labelled pending
fixture. It does not approve leave or validate entitlements/holidays. Decision
history is verified in both published HR and Superior apps, using an explicit
Power Apps grid with editing/navigation/multi-select disabled.

## Latest development: employee menu and balance foundations

The published Employee self service app hides Attachments, Employees, Leave
Types, Leave Approvals and Timesheets. The welcome menu shows Leave Requests
and Expense Claims. See `employee-menu-hidden-after-publish.jpg` and
`IMPLEMENTATION_STATUS.md`.

`BALANCE_IMPLEMENTATION.md` records the live annual grant action, approved
Annual Leave deductions and immutable balance write protection.
Calendar periods and anniversary rates are confirmed; first/last-year rules
remain held. The baseline Create submission checks were deployed to the
existing synchronous Leave.Plugins guard on 11 October 2026. Live SDK checks
rejected missing type/date and reversed dates with no persisted records, and
saved a valid fictional request as Pending Superior. Dataverse DateOnly fields
normalize supplied time before the plug-in sees it; the local test still checks
the plug-in's fail-closed behavior if a time reaches it. This deployment does
not complete the employee Apply Leave screen or category-specific submission
rules. The earlier DLL snapshot in `deployment-2026-10-09.json` predates this
assembly update.

## Current-year annual grants

The secured `cr2c9_GrantCalendarAnnualLeave` action is live; see
`BALANCE_IMPLEMENTATION.md`. HR Employee → Annual balance provides an authorized
grant button. `annual-grant-deploy` updates only Leave.BalanceGuards; never upload
the locally modified main Leave.Plugins DLL without reviewing its undeployed
submission changes. `annual-grant-ui` publishes the HR control.
`annual-grant-regression` verifies the existing fictional fixture;
`annual-grant-test` requires a fresh fixture without a period.
`hr-config-inspect` verifies the configured notification recipients without
sending a message. Historical balance staging commands are obsolete.
Approved Annual Leave deductions are active in the development environment.
The synchronous approval step rejects incomplete dates and insufficient
balances, and appends exact debits in the decision transaction. The HR Annual
balance control reflects consumed hours after refresh. Cancellation and reversal
remain pending. See `annual-approval-accounting-2026-10-10.json` for the live
step and `annual-approval-balance-zero-live.png` for the verified HR display.

**Grant calendar annual leave balances**, `61002ed5-7150-750a-b966-a4708e51c6fb`,
is On in Leaveandclaimhub: daily06:30 Malaysia time, active Employee IDs,
sequential bound grant calls, pagination5000 (current plan limit). Both the
fictional-only and active-employee manual tests succeeded. No email actions.
