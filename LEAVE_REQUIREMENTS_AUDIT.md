# Leave requirements audit

Reviewed 9 October 2026, Asia/Kuala_Lumpur.

This is the original audit snapshot. Subsequent live schema, calendar-date and
server safeguard changes are tracked in `scripts/dataverse/IMPLEMENTATION_STATUS.md`.
The original gap assessments below are retained as the baseline, not the latest
deployment status.

Latest scope correction after sync inspection: HR input is required to maintain
employment details that the sync does not provide. This restores HR profile
maintenance and supersedes the intermediate sync-only decision. Skip entitlement
calculations when required inputs are null, pending HR details. Current policy
and implementation status supersede the baseline assessments below.

The Power Apps system has a working Superior decision form and a tested HR email
notification. It does not yet demonstrate the complete leave lifecycle in the
specification. The most important gaps are data-layer enforcement, consistent
calendar dates, entitlement and balance accounting, cancellation, employee
notifications, and HR acknowledgement.

## Scope and evidence

Source: `/Users/amirul/Downloads/Leave Management and Claim Management - Process Flow and Functional Specification.docx`.
All Leave Management content, including both process diagrams, was read and
visually reviewed on rendered pages 1–12. Claim Management on pages 13–15 is
excluded. Page numbers below refer to this audit's render and may differ from
Word's pagination on another machine.

Compared with `LEAVE_POLICY.md`, `scripts/dataverse/README.md`, the current
`cr2c9_leaveRequestForm.js`, previous live test results, and read-only inspection
of the live maker schema, employee canvas app, HR app navigation, and cloud-flow
inventory. The local Next.js/PostgreSQL prototype was inspected separately and
is not credited as Power Apps functionality.

Status definitions: **Partial** means some behavior exists but does not satisfy
the complete requirement. **Gap** means the inspected implementation does not
provide it. **Unverified** means sufficient evidence is unavailable. **Conflict**
means the specification differs from earlier confirmed decisions or needs
correction. This is not a full export of every tenant solution, plug-in or security
role. The current signed-in account has development privileges, so seeing other
employees' records does not prove what an ordinary Employee can access.

## What is working

- Four apps exist: Employee self service, Superior review workspace, HR leave
  operations, and System administration. Their existence alone does not prove
  role permissions or complete workflows.
- Employee data includes Entra ID, email, joined date, annual leave entitlement
  days, active flag and Superior lookup. Daily Microsoft 365 synchronization exists.
- The published Information form checks the latest successfully synced Superior
  on load and before saving. It blocks self-approval, missing or mismatched
  Superior, stale status and retrieval failures; only Pending Superior can be
  decided. It saves approver identity, name, time and comments, then locks the form.
- Maria's authorized dummy LR-0006 was saved as Approved with decision stamps.
  Maria's temporary Superior was cleared. Already-decided and blank-Superior
  behavior was checked. Thirteen mocked tests passed. A separate-account live
  access test remains outstanding.
- The enabled HR notification flow triggers on status updates to Approved with
  decision stamps. Its Outlook action succeeded with HTTP 200 on 9 October at
  12:09 MYT using temporary recipient `muhammad.amirul@asteriskdigital.com.my`.
  Inbox receipt was not independently checked.

## Requirement matrix

| Reference | Requirement | Current assessment | Required work |
| --- | --- | --- | --- |
| §1, p1 | Employee applies, submits and tracks leave | Partial | Replace the generated table experience with an Apply Leave form, clear validation and personal request list. Verify the complete submit-to-decision path. |
| §2, p1 | Employees view own requests, balances and decision history | Partial | Requests/status are visible; selected-leave balance and decision history were not present on the inspected canvas screen. Add both and enforce personal access. |
| §2, p1 | Employee access only own records; employees can view others' leave | Conflict / Unverified | Define a separate shared absence calendar with allowed fields. Keep reasons, certificates, balances and decision details private. Test ordinary employee access. |
| §2, p1 | Superiors review assigned staff and have Employee capabilities | Partial | Form checks assignment. Add a filtered pending queue and personal employee functions; verify data access and manager reassignment for existing requests. |
| §2, p1 | HR views all leave and balances, acknowledges, manages accounts/roles/configuration | Partial / Conflict | HR app has Employees, Leave Requests and Leave Types navigation. Balance operations and acknowledgement are not demonstrated. Account provisioning is not proven by an Employee row. Resolve HR role-assignment powers against earlier Admin-only privileged role policy. |
| §2, p1 | Data-layer access restrictions | Gap in implemented approval protection; overall security roles unverified | Enforce record access in Dataverse and validate decision/cancellation transitions on the server. UI filtering and disabled controls are insufficient. |
| LR-01, p7 | Annual entitlement based on profile level | Gap / Conflict | Add employee level and effective entitlement rules. Document has 14/15/18-day bands; earlier decision was a universal 14-day starting allowance subject to statutory floor. |
| LR-02, p7 | Reward based on completed service | Gap | Implement completed-year calculation and effective date. Document defines +0/+1/+2/+3 for <2/2–<4/4–<6/6–<8 years; 8+ is unspecified. |
| LR-03, p7 | First/last-year proration | Gap / Incomplete specification | Add leaving date and agreed calculation, period, rounding and service-anniversary handling. The referenced calculation rules are not supplied. |
| LR-04, p7 | Medical entitlement by employment type and service | Gap | Add employment type and separate statutory/company rules. Full-time bands are 14/18/22; blanket part-time exclusion needs correction. |
| LR-05, p7 | Show selected-type entitlement and available balance | Gap | Add an employee/leave-type/period balance model and display opening entitlement, used amount, carry-forward and available balance. A single Annual Leave Entitlement Days field is not a balance engine. |
| LR-06, p7; BR-01, p11 | Validate selected leave type and entitlement | Gap | Build category-specific eligibility, date counting, document and balance validation. Existing review JS validates reviewer authority, not leave eligibility. |
| §4.1.3, p6; BR-05, p11 | Exactly 4 or 8 annual leave hours per working date | Conflict / Gap | Current request stores a decimal Hours Per Day. Earlier policy permits 1–8 including fractions; local prototype accepts flexible hours. Confirm governing rule, then validate server-side and support mixed half/full days within a multi-date request. |
| §5 diagram, p8 | Deduct balance when approved | Gap | Approval test saved status/stamps only. Add one authoritative deduction transaction linked to the request; pending requests must not be treated as approved deductions. Define any pending reservation separately. |
| §5 diagram, p8 | Notify Employee on rejection and Employee plus HR on approval | Partial | Existing flow notifies HR only for approval. Add employee approval/rejection notifications and verify correct recipient lookup. |
| §6, p9 | Pending Superior → Approved/Rejected; Cancelled after approved cancellation | Partial | Approval/rejection works at form level. Cancellation workflow is missing. Preserve legacy Draft/Pending HR data without exposing obsolete approval steps for new requests. |
| CAN-01, p11 | Own approved request only can be cancelled | Gap | Add a cancellation action with server-side employee ownership and original Approved-status checks. |
| CAN-02, p11 | Assigned Superior decides cancellation | Gap | Add a separate cancellation decision with assignment and self-approval enforcement. |
| CAN-03, p11 | Original remains Approved and deduction remains while cancellation pending | Gap | Store cancellation state separately; do not change original status or restore balance on submission. |
| CAN-04, p11 | Approved cancellation immediately cancels original and restores deducted balance | Gap | Perform cancellation and reversal together, restoring the exact original deduction and its original balance buckets. |
| CAN-05, p11 | HR/Admin acknowledges after cancellation takes effect | Gap | Add acknowledgement identity/time/state separately from the leave decision. It must not delay cancellation or restoration. |
| CAN-06, p11 | No duplicate cancellation/restoration | Gap | Enforce one active cancellation and unique reversal per deduction; handle retries and concurrent decisions. |
| CAN-07, p11 | Preserve original, cancellation and every decision | Partial | Leave Approval table exists historically; current JS only stamps the request. Implement linked append-only decision/cancellation/balance history and verify Dataverse auditing. |
| BR-02, p11 | Only assigned Superior decides | Partial | Implemented in one form against daily synced assignment; server enforcement and alternate app paths are not verified. |
| BR-03, p11 | No self-approval | Partial | Implemented/tested in JS; add server guard and separate-account integration tests. |
| BR-04, p11 | Decision only while pending | Partial | Form checks saved pending status before save; simultaneous or direct updates still need server concurrency protection. |
| BR-06, p11 | Required fields/documents before submission | Partial / Gap | Some schema-required fields exist. No Leave Request business rules were listed. Attachment is a Single line of text column, not evidence of certificate upload or validation. Add secured files and category-specific checks. |
| BR-07, p11 | Approval/deduction/cancellation/restoration once | Gap | Add unique operation identifiers and transactional/concurrency checks. HR flow can also send duplicates on repeated Approved writes. |
| §9, pp11–12 | Missing details, invalid date order/duration explain errors | Unverified / Gap in inspected logic | Review JS contains no submission date/duration rules, and no table business rules were listed. Add and test validation in both employee UI and server. |
| §9, p12 | Overlap rule wording | Incomplete specification | It says “Do not apply the overlap rule once confirmed.” Clarify the intended meaning; do not introduce a blanket rejection rule. The local prototype currently limits combined daily hours. |
| §9, p12 | Absent Superior replacement/delegation | Gap / Undefined | Daily manager sync supports reassignment after sync, not absence delegation. Define delegate, duration, audit trail and self-approval constraints. |

## Leave category coverage

The inspected Leave Type data contains only Annual Leave, Sick Leave,
Hospitalisation Leave, Maternity Leave and Paternity Leave. Its custom columns
are Name, Description and Is Hourly Based. That flag does not encode the rules below.

| Category from §3, pp2–5 | Work needed |
| --- | --- |
| Annual | Position/service entitlement, period/proration, working-date calculation, allowed per-date duration, balance and deduction. |
| Carry forward | Track prior-year bucket, 5-day normal cap, 31 March usage limit, Director exception request before year-end, consumption order and expiry. Check against statutory timing before implementing forfeiture. |
| Medical | Full-time service bands, employment classification, certification, schedule/holiday handling and medical notifications. Map the existing Sick Leave name consistently. |
| Hospitalisation | Separate 60-day statutory balance; qualifying certification. Correct document's inclusion of prior medical leave. |
| Marriage | Add category; first legal marriage, 3 consecutive days, probation completed, registration date within 12 months, certificate. |
| Maternity | Implement 98 consecutive calendar days and appropriate notice/eligibility/allowance treatment. Review blanket exclusions and 60-day advance-notice wording. |
| Paternity | Implement 7 consecutive calendar days, service, marriage, confinement count and notification conditions. Review employment-type exclusions and post-birth notice exception. |
| Compassionate | Add category; 3 consecutive days, allowed relationship and supporting evidence. |
| Emergency | Add classification linked to Annual balance; permit post-event formal application/evidence according to defined timing. |
| Training | Add classification and company-required versus employee-requested external training. Only the latter consumes Annual balance under the document. |
| Unpaid | Add category and written request/exhausted-paid-leave checks. Document lists HR as approver, conflicting with generic Superior-only workflow. |
| Replacement | Add holiday/schedule calendar and credit/use tracking. Document specifies Saturday credit within 30 days, Sunday substitution and no banking; clarify expiry/booking interpretation and statutory substitutions. |
| Off in lieu | Add approved-work evidence, earned credits, Superior approval and use rules. Earning rate, eligibility and expiry are unspecified. |

## Concrete issues and document conflicts

1. **Calendar date discrepancy.** LR-0006 shows 7 October 2026 in the canvas app
   and maker grid but 6 October in the Superior app. Start Date is Date only
   format with User local timezone adjustment. This is a likely contributor;
   End Date and Date Joined require the same inspection. Use calendar-date
   behavior consistently and plan existing-record conversion before changing
   it. Keep Decision On as an actual timestamp, with consistent MYT display.
   Microsoft advises avoiding Date only format with User local behavior:
   https://learn.microsoft.com/en-us/power-apps/maker/data-platform/behavior-format-date-time-field
2. **Employee app is still generic.** It exposes table cards including Employees,
   Attachments and Leave Approvals; its inspected leave screen has no selected-type
   balance, decision stamps/history, or cancellation request action. An admin
   can see all sample leave requests there. Do not infer an ordinary employee
   can access them; verify role-based access before designing a shared calendar.
3. **Missing profile inputs.** Inspected custom Employee columns have no employee
   level, employment type, probation completion or leaving date. These are
   needed before entitlement/eligibility rules can work. Derive sensitive event
   eligibility from protected request-specific records rather than exposing it
   in a general employee directory.
4. **Hospitalisation rule needs correction.** JTKSM explicitly separates ordinary
   sick leave and hospitalisation leave. Preserve separate balances rather than
   subtracting medical leave from the 60 days:
   https://jtksm.mohr.gov.my/ms/soalan-lazim/akta-kerja-1955-pindaan-2022/cuti-sakit/mengapakan-kelayakan-cuti-sakit-dan-cuti-sakit
5. **Blanket part-time exclusions need correction/review.** Malaysia's Part-time
   Employees Regulations provide paid sick leave and holiday entitlements for
   covered part-time employees. Do not classify everyone called part-time or
   intern as ineligible. Determine legal employment coverage and keep maternity
   leave eligibility separate from allowance eligibility:
   https://jtksm.mohr.gov.my/sites/default/files/2023-03/10.%20Employment%20-%20Part-time%20Employees%20-%20Regulations%202010%20%20%281%29.pdf
6. **Earlier company decisions differ.** Earlier confirmed policy has 14-day
   starting AL subject to statutory floor; document has 14/15/18 plus service
   rewards. Earlier policy permits 1–8 fractional hours; document only 4/8.
   Earlier policy reserves privileged roles for Admin; document says HR manages
   roles. Record the resolution before implementation. The document's new
   cancellation sequence is clear for future approved leave, but treatment of
   already-taken/in-progress/partial cancellation and pending withdrawal is not.
7. **Acknowledgement is distinct from notification.** Receiving an email does
   not satisfy recorded HR acknowledgement. Keep acknowledgement separate;
   Superior approval remains immediately effective.
8. **Approver exceptions and wording need decisions.** Unpaid lists HR approval;
   carry-forward exceptions list Superior/Director; generic rules say only
   Superior. Resolve those routes. Replacement contains an apparent copied
   training rule. Proration formula, 8+ service reward, half-day AM/PM, holiday
   schedules, overlap wording and absence delegation also need specification.

No company policy or live configuration was changed by this audit.

## Recommended implementation order

1. Resolve policy conflicts and correct the legal/specification inconsistencies.
   Approve an explicit rule catalogue, effective dates and role matrix.
2. Fix date representation with a record-conversion plan. Implement and test
   Dataverse record access and server-side transition/self-approval checks.
   Separate ordinary Employee, Superior, HR and Admin sign-ins are necessary.
3. Add profile inputs, effective entitlement/policy configuration, balance
   periods, holiday/schedule data and a transaction history. Implement deduction
   and reversal once per request using server concurrency protection.
4. Build employee Apply Leave, per-date duration, document upload, entitlement/
   balance display and personal history. Reuse consistent rules on the server.
5. Implement all CAN-01–07 with linked cancellation records and exact reversals.
6. Add employee notifications, HR acknowledgement queues/stamps, permanent HR
   recipient, notification deduplication and retry/failure visibility.
7. Complete remaining categories, carry-forward exceptions and delegation.
   Run end-to-end role, boundary-date, entitlement, simultaneous-decision,
   flow-retry, cancellation and history tests before production use.

Suggested design, not an existing implementation: retain Leave Request as the
parent; add Leave Request Date rows for mixed per-date durations; add effective
Leave Policy/Entitlement and Balance Transaction records; add Cancellation
Request and Decision History; record HR acknowledgement separately. Enforce
the decision, audit entry and balance movement together in a server operation.
Dataverse roles provide record privileges, while business transitions require
server logic; a synchronous plug-in/custom operation is an implementation option.
See https://learn.microsoft.com/en-us/power-platform/admin/security-roles-privileges
and https://learn.microsoft.com/en-us/power-apps/developer/data-platform/plug-ins.

## Separate local prototype

The Next.js/PostgreSQL prototype has leave entries, timesheets and some balance/
service calculations, but these do not run in Power Apps. It still reflects
earlier policy choices, including flexible hourly AL and HR leave approval, has
demo authentication and fixed 2026 calculations, and does not implement the new
cancellation lifecycle. Medical-day handling and overlap limits also need
comparison with the final rule catalogue. Do not migrate it as compliant logic
without reconciliation. Timesheets are an earlier company requirement, not an
explicit requirement in this Word document's Leave Management section.
