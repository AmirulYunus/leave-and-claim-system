# Entitlement input inspection

Inspected 9 October 2026 using read-only FetchXML against the live Dataverse
environment. No flow edits, manual runs, profile changes, or entitlement writes
were performed. This is a mapping assessment, not a data backup.

## User decision after inspection

HR actions are required to update the employee employment details missing from
sync. This supersedes the earlier sync-only profile scope. Joining date,
position/level, employment type and applicable probation details require HR
maintenance; directory identity and Superior remain sync-maintained. The sync
must preserve HR inputs. No corresponding live permissions or flow changes have
been made by this documentation update.

The user also directed that employees with null calculation inputs should not be
counted. Treat a missing required input as an incomplete calculation, pending HR
details; skip writing a new entitlement rather than substituting zero or a
default. Preserve existing balance/history. Required inputs depend on the
particular entitlement: optional/unrelated fields are not calculation gates.

Subsequently the user selected service-anniversary updates. A live read-only rate
preview now implements current full-year rates and null-input handling; see
`IMPLEMENTATION_STATUS.md`. The counts below describe the earlier inspection,
before the fictional calculation fixture received employment inputs.

## Daily flow

**Sync Employees from Microsoft 365** is Activated. Its Dataverse workflow row
is `0b6f0a80-cac2-f111-a05a-000d3a826b69`; this is distinct from the previously
recorded Power Automate flow identifier `8f9df3ab-3093-9122-497d-479fc8f52b5a`.
The stored definition schedules recurrence daily at 06:00 Singapore Standard
Time (UTC+8). Run success and tenant source-field contents were not inspected.

The definition uses Office 365 Users Search for users (V2), with top 999 and no
required search term. It filters enabled users to the company UPN domain and
excludes two explicit directory identities. These checks are not an employment
type or leave-policy classification.

| Destination/input | Mapping found | Entitlement implication |
| --- | --- | --- |
| Entra user ID | Search result Id on new-row creation | Identity link exists; does not establish service dates or policy level. |
| Employee name | DisplayName on new-row creation | Informational only. |
| Email | Mail, falling back to UserPrincipalName, on creation | Informational/notification input only. |
| Active flag | Literal true on creation after source filtering | Existing employees are not deactivated by the inspected definition. |
| Superior | Separate Get manager (V2) loop, resolved by Entra ID to Employee lookup | Approval input; no entitlement rule. Source 404 has an explicit clear-lookup path. |
| Position | No mapping | JobTitle is a documented SearchUserV2 output; actual tenant values and title-to-level mapping remain unverified. |
| Date Joined | No mapping | Required to calculate completed service; existing dates cannot be credited to this sync. |
| Employment Type | No mapping | Full-time/part-time/intern classification requires a confirmed source and mapping. |
| Employee Level | No mapping | Required for the company's 14/14/15/18-day annual base. HR-maintained under the latest correction; any automatic title-to-level mapping needs confirmation. |
| Probation Completed On | No mapping | Needed for probation-dependent category eligibility; source unconfirmed. |
| Leaving date | No mapping | Relevant to held last-year proration; do not add calculations now. |
| Annual entitlement | No mapping/calculation | Legacy dummy entitlement values are not the calculation rule. |

Both existing-employee Update a row actions contain only entityName and recordId,
with **no item fields**. They do not refresh name/email/active status, and the
email-match branch does not assign Entra ID. The separate manager loop updates
Superior only. Record this for the later sync repair; inspection did not modify
the flow.

## Aggregate completeness

All Employee rows, including dummy development fixtures: 27 total; 22 have an
Entra ID; 6 have Date Joined; 9 have Superior; **0** have Position, Employment
Type, Employee Level, or Probation Completed On. These counts do not establish
that a row was created by the sync, or that its values are authoritative.

## Calculation rules to work through with the user

Start with annual entitlement for the supported, confirmed cases:

1. Resolve the employee's verified level to base days: 14 / 14 / 15 / 18.
2. Calculate completed continuous service using the actual employment joining
   date and today's Malaysia calendar date. Account creation date is not
   a substitute for employment joining date.
3. For service below eight years, add the documented reward: +0 below two;
   +1 from two to below four; +2 from four to below six; +3 from six to below eight.
4. Preserve the applicable statutory annual minimum from LEAVE_POLICY.md.
   Proposed full-allowance formula: max(company base + service reward,
   statutory minimum). Assessment date and anniversary changes are confirmed;
   calendar-year annual balance periods were confirmed on 10 October. This is
   not a proration/accrual formula.
5. Convert a confirmed day allowance to hours at eight hours per standard day
   when accounting for annual leave. Per-date use remains exactly four or eight
   hours under the latest company decision.

Eight-year-and-later rewards and first/last-year proration/rounding remain held.
Medical entitlement needs both verified service date and employment type;
probation and family-event inputs belong to later category eligibility work.
At inspection no calculations were deployed. Subsequently a rate preview was
deployed; period grants and balance accounting remain pending. Assessment now
uses today's Malaysia date, with service-based rate changes on anniversaries.

The joining-date source question is resolved by the user's correction: restore
HR-maintained employment details. Before implementing calculations, define each
calculation's required inputs and enforce the null-input skip rule. Annual
assessment date/anniversary handling are now confirmed. Balance periods and
proration still need agreement.

## Microsoft source distinctions

- [Office 365 Users connector](https://learn.microsoft.com/en-us/connectors/office365users/)
  documents JobTitle in SearchUserV2's User output. A documented connector field
  does not prove that company users have populated values.
- [Microsoft Graph user](https://learn.microsoft.com/en-us/graph/api/resources/user?view=graph-rest-1.0)
  distinguishes employeeHireDate (employment start) from createdDateTime
  (directory account creation). employeeHireDate and employeeType require
  explicit selection. Their existence in Graph does not prove that the current
  connector/action reads them or that this tenant maintains them.
- Graph employeeType is a free-text worker classification, not an automatic
  mapping to the application's Full time / Part time / Intern choices.
- The connector's default profile hireDate is not evidence of a populated
  authoritative employeeHireDate; verify the chosen source before mapping.

Statutory rules and confirmed company decisions remain in ../../LEAVE_POLICY.md.
The JTKSM PDF endpoints returned HTTP 503 during this inspection, so no new legal
conclusions or implementation changes were made from those failed reads.
