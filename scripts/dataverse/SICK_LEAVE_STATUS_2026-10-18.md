# Sick Leave development status — 18 October 2026

Scope: Developer Dataverse environment only. This is not a production deployment record.

## Deployed and verified

- Full-time Sick Leave has a separate calendar-year balance and protected grant action (`cr2c9_GrantCalendarSickLeave`). The allowance follows the confirmed 14/18/22-day service bands; repeat grants add only a service-based increase.
- A new Sick Leave request must be submitted by midnight Malaysia time at the start of the third calendar day after its Start Date. This is the user's strict 48-hour rule measured from 00:00 on the Start Date.
- Approval requires an uploaded Note attachment and a timely original app submission. The Superior or HR must still judge whether the file is a valid medical certificate.
- Approval writes the decision and exact 4/8-hour weekday debit in one Dataverse transaction. Future approved Sick cancellation uses the existing exact reversal route.
- The fictional live fixture passed: approval without a file rejected with no debit or decision; approval after a fictional test file created one decision and one 8-hour debit; Sick balance changed from 176 to 168 hours. A repeat grant added zero hours. The test file is not a real medical certificate.
- Local rule and accounting checks pass. The fixture does not replace a separate ordinary-role test or a concurrent-decision test.

## Still pending

- The existing daily Annual Leave grant flow does **not** call the Sick grant yet. Its new designer rejected the bound action while saving: `Bound action 'cr2c9_GrantCalendarSickLeave' is not found in the Organization or is not included in the Solution being imported.` The classic designer also listed only the older actions; typing the Sick action as a custom value left Action Name unresolved and failed flow validation. Dataverse SDK calls work, and a direct Web API `$metadata` check returned HTTP 200 with both Annual and Sick actions bound to Employee. Both actions are in the same solution with equivalent bound-action settings. This narrows the discrepancy to Power Automate connector metadata/validation, although its precise cause remains unconfirmed. All failed draft steps were removed and the original Annual flow was saved successfully. Do not describe Sick grants as automatic until a flow run has been verified.
- Employee-facing Sick balance display and clear form help about the 48-hour deadline and medical certificate.
- Ordinary Employee/Superior/HR account verification, document-access verification, and real end-to-end approval through the published form.
- Production solution import, environment-specific category/identity mapping, and production role/app sharing.

## 19 October update

- Publishing the Sick bound-action metadata made it selectable in Power Automate. The Developer daily grant flow now calls both Annual and Sick grant actions in the same employee loop. A manual flow run on 18 October at 19:04 Malaysia time succeeded for 29 active employees. One inspected Sick result was `PendingHrDetails` (Date Joined and Employment Type missing), with zero credited hours; this is an explicit hold, not a successful grant. Inspect an eligible employee's output and ledger before claiming the automatic Sick credit is fully verified.
- The earlier “daily flow does not call Sick grant” item above is historical and superseded by this update.
- **Eligible-employee verification passed.** In the [19:04 Malaysia-time test run](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/flows/61002ed5-7150-750a-b966-a4708e51c6fb/runs/08584092854444367917613253372CU10?v3=false), the Sick action for Muhammad Amirul returned `Granted`, `CreditedHours: 112`, and `AvailableHours: 112` for 2026. The [23:11 repeat run](https://make.powerautomate.com/environments/0482c6ce-0b4e-e985-a55d-d19616dc4891/flows/61002ed5-7150-750a-b966-a4708e51c6fb/runs/08584092705876328649042078613cU14?v3=false) returned `AlreadyGranted`, `CreditedHours: 0`, `AvailableHours: 112` for the same Employee. A read-only Dataverse audit independently found one exact January–December 2026 Sick balance period, one 112-hour grant ledger entry created at 11:04:25 UTC on 18 October (19:04:25 Malaysia), no debit/reversal, and 112 hours available. The calculated full-time Sick rate is 14 days × 8 hours = 112 hours. This verifies the manual flow credit and repeat behavior; the first scheduled run after this change remains to be checked.
