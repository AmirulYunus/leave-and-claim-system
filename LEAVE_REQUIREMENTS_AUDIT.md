# Leave requirements audit

Reviewed 9 October 2026, Asia/Kuala_Lumpur. Requirements from the leave specification, with the company decisions that later resolved conflicts. Current policy in `LEAVE_POLICY.md` and `docs/leave-rules/` supersedes anything here.

## Scope

Source: `Leave Management and Claim Management - Process Flow and Functional Specification.docx`. All Leave Management content, including both process diagrams, was read on rendered pages 1–12. Claim Management on pages 13–15 is excluded. Page numbers refer to this audit's render and may differ from Word's pagination.

## Requirement matrix

| Reference | Requirement | Resolution or note |
| --- | --- | --- |
| §1, p1 | Employee applies, submits and tracks leave | One main request form per category (16 October decision). |
| §2, p1 | Employees view own requests, balances and decision history | Enforce personal access on the server. |
| §2, p1 | Employee access only own records; employees can view others' leave | Shared absence calendar with name, dates and half/full-day only (23 October decision). Reasons, certificates, balances and decisions stay private. |
| §2, p1 | Superiors review assigned staff | Superseded: there is no Superior role; HR decides (6 November decision). |
| §2, p1 | HR views all leave and balances, acknowledges, manages accounts/roles/configuration | Acknowledgement removed (10 October). HR creates HR and Employee accounts; Admin creates HR accounts (6 November). |
| §2, p1 | Data-layer access restrictions | Enforce record access and decision/cancellation transitions on the server; UI filtering alone is insufficient. |
| LR-01, p7 | Annual entitlement based on profile level | 14/14/15/18-day base by level, subject to statutory floor. |
| LR-02, p7 | Reward based on completed service | +0/+1/+2/+3 for <2/2–<4/4–<6/6–<8 years; 8+ years on hold. |
| LR-03, p7 | First/last-year proration | First year: completed-month accrual, rounded up once to a half day (14 October / 3 November). Last year on hold. |
| LR-04, p7 | Medical entitlement by employment type and service | Full-time 14/18/22 days. Do not apply a blanket part-time exclusion. |
| LR-05, p7 | Show selected-type entitlement and available balance | Show opening entitlement, used amount, carry-forward and available balance per period. |
| LR-06, p7; BR-01, p11 | Validate selected leave type and entitlement | Category-specific eligibility, date counting, document and balance validation. |
| §4.1.3, p6; BR-05, p11 | Exactly 4 or 8 annual leave hours per working date | Adopted; supersedes the earlier 1–8 fractional-hours rule. |
| §5 diagram, p8 | Deduct balance when approved | One authoritative deduction transaction linked to the request; pending requests are not approved deductions. |
| §5 diagram, p8 | Notify Employee on rejection and Employee plus HR on approval | Every decision emails the employee and HR (6 November). |
| §6, p9 | Pending → Approved/Rejected; Cancelled after approved cancellation | HR makes the decision. |
| CAN-01, p11 | Own approved request only can be cancelled | Server-side ownership and Approved-status checks. |
| CAN-02, p11 | Assigned Superior decides cancellation | Superseded: HR decides; no self-decision. |
| CAN-03, p11 | Original remains Approved and deduction remains while cancellation pending | Store cancellation state separately. |
| CAN-04, p11 | Approved cancellation immediately cancels original and restores deducted balance | Restore the exact original deduction and its balance buckets. |
| CAN-05, p11 | HR/Admin acknowledges after cancellation takes effect | Superseded: notification only (10 October). |
| CAN-06, p11 | No duplicate cancellation/restoration | One active cancellation and a unique reversal per deduction, safe under retries and concurrent decisions. |
| CAN-07, p11 | Preserve original, cancellation and every decision | Append-only decision, cancellation and balance history. |
| BR-02, p11 | Only assigned Superior decides | Superseded: HR decides. |
| BR-03, p11 | No self-approval | Enforce on the server. |
| BR-04, p11 | Decision only while pending | Enforce on the server with concurrency protection. |
| BR-06, p11 | Required fields/documents before submission | Secured file uploads and category-specific checks. |
| BR-07, p11 | Approval/deduction/cancellation/restoration once | Unique operation identifiers and transactional checks; avoid duplicate notifications on repeated writes. |
| §9, pp11–12 | Missing details, invalid date order/duration explain errors | Validate in both the UI and the server. |
| §9, p12 | Overlap rule wording | Says “Do not apply the overlap rule once confirmed.” Clarify the meaning; do not introduce a blanket rejection rule. |
| §9, p12 | Absent Superior replacement/delegation | Moot: HR decides regardless of any manager's absence. |

## Leave category coverage

| Category from §3, pp2–5 | Specification rules |
| --- | --- |
| Annual | Position/service entitlement, period/proration, working-date calculation, allowed per-date duration, balance and deduction. |
| Carry forward | Prior-year bucket, 5-day normal cap, 31 March usage limit, exception request before year-end, consumption order and expiry. Check against statutory timing before implementing forfeiture. |
| Medical | Full-time service bands, employment classification, certification, schedule handling and notifications. |
| Hospitalisation | Separate 60-day statutory balance; qualifying certification. See the 19 October decision on the company's combined figure. |
| Marriage | First legal marriage, 3 consecutive days, probation completed, registration date within 12 months, certificate. |
| Maternity | 98 consecutive calendar days and appropriate notice/eligibility/allowance treatment. Review blanket exclusions and 60-day advance-notice wording. |
| Paternity | 7 consecutive calendar days, service, marriage, confinement count and notification conditions. Review employment-type exclusions and post-birth notice exception. |
| Compassionate | 3 consecutive days, allowed relationship and supporting evidence. |
| Emergency | Linked to Annual balance; post-event application and evidence permitted. |
| Training | Company-required versus employee-requested external training; only the latter consumes Annual balance. |
| Unpaid | Written request. The paid-leave-exhaustion check is not enforced (26 October). |
| Replacement | Saturday credit within 30 days, Sunday substitution and no banking in the document. Deferred by the company. |
| Off in lieu | Approved-work evidence, earned credits and use rules. Earning rate, eligibility and expiry are unspecified. |

## Specification issues

1. **Hospitalisation rule needs correction.** JTKSM separates ordinary sick
   leave and hospitalisation leave. Preserve separate balances rather than
   subtracting medical leave from the 60 days:
   https://jtksm.mohr.gov.my/ms/soalan-lazim/akta-kerja-1955-pindaan-2022/cuti-sakit/mengapakan-kelayakan-cuti-sakit-dan-cuti-sakit
2. **Blanket part-time exclusions need review.** Malaysia's Part-time
   Employees Regulations provide paid sick leave and holiday entitlements for
   covered part-time employees. Do not classify everyone called part-time or
   intern as ineligible. Determine legal employment coverage and keep maternity
   leave eligibility separate from allowance eligibility:
   https://jtksm.mohr.gov.my/sites/default/files/2023-03/10.%20Employment%20-%20Part-time%20Employees%20-%20Regulations%202010%20%20%281%29.pdf
3. **Earlier company decisions differed.** The universal 14-day starting AL and
   1–8 fractional hours were superseded by the document's 14/15/18 base with
   service rewards and 4/8-hour durations. Treatment of already-taken,
   in-progress or partial cancellation and pending withdrawal remains
   unspecified.
4. **Acknowledgement is distinct from notification.** The company removed
   acknowledgement; HR is notified by email only.
5. **Wording needs decisions.** Replacement contains an apparent copied
   training rule. Half-day AM/PM, holiday schedules and overlap wording also
   need specification.

## Suggested design

Keep Leave Request as the parent, with per-date rows for durations. Add
entitlement/policy configuration and balance transaction records, a
cancellation request, and decision history. Commit the decision, audit entry
and balance movement together in one server transaction.
