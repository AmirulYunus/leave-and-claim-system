# Role dashboard and classification deployment status — 2026-11-04

Developer environment only. No fictional leave requests were created.

## Published and verified through the Dataverse development tool

- Organization-owned HR-maintained Department and Cost Centre tables.
- Optional Department and Cost Centre lookup columns on Leave Request and Public Leave Absence.
- Read access for Employee, Superior, HR and Calendar Reader roles; HR configuration write access.
- Superior views: Summary Leave Request (all assigned), Pending Approval, Approved Leave, Upcoming Leave, pending cancellations. FetchXML verifies 50/24/9/3/0 records for the signed-in developer. The request views include both classification columns. Upcoming sorts by leave start date; others sort by submission date.
- HR views: All Leave Requests, Pending Approval, Approved Leave, Upcoming Leave, Approved Leave This Month/Year. FetchXML verifies 94/55/15/4/8/15 records for the signed-in developer. The request views include both classification columns.
- Department and Cost Centre shown read-only on the Superior review form.
- Updated server plug-in published and hash-verified so pending edits accept valid active classification options, and newly approved public calendar entries carry classification lookups.
- Audited five existing public absences; none had classification on the linked request, so no backfill was needed.
- Redeployed and verified global Append To access on Department and Cost Centre for leave roles.
- Republished the corrected dashboard resource. Added it to HR and Superior app navigation and published both apps. The HR preview loaded 55 pending approvals, 8 approved requests overlapping November 2026, and 4 upcoming leaves; the Superior preview loaded only assigned-team data (24, 4, and 3 respectively).
- Added Department and Cost Centre pages to HR navigation and published the app. Both pages show an active-record view and a New action.
- Added Department and Cost Centre lookup cards to the employee Canvas leave request form. Each dropdown is filtered to active options; Studio reports no formula errors in the new cards. Studio confirmed "Publish successful."

## Live verification on 2026-11-05

- HR app loaded its dashboard with date, Department, and Cost Centre filters and its Department and Cost Centre navigation pages. The live dashboard showed 55 pending, 8 approved in November, and 4 upcoming.
- Superior app loaded its dashboard with Department and Cost Centre filters. A live account with only the Superior role has not yet been used for verification.
- Darwisyah's published employee form now shows both Department and Cost Centre lookups after a fresh publication. A blank request was opened and cancelled; no request was submitted.
- Added labelled Department and Cost Centre dropdowns to the employee public calendar and published the Canvas app. The calendar expression references both lookups and Studio reports no formula errors. After refreshing to the latest version as Darwisyah, the published calendar displayed both filter labels and retained the existing leave entries with blank filters.
- HR created active, clearly labelled Developer-only options: `DEV TEST – Department A/B` and `DEV TEST – Cost Centre A/B`. Both appeared in HR lists, HR dashboard filters, and Darwisyah's calendar filters. Filtering either classification returned zero existing leave records, as expected because those records have no classification; clearing the filters restored the existing calendar entries.
- The employee calendar filters overlapped the calendar heading and lacked a reset action. Repositioned the controls and added **Clear filters**, then saved, published, refreshed Darwisyah's app, and verified that reset restores her leave entries.
- Darwisyah's blank new-request form initially showed generic or blank lookup choices despite the active options. Corrected both Combo Boxes: Items adds an `OptionLabel` column from the HR-maintained name, and DisplayFields and SearchFields use `OptionLabel`. Studio app checker showed no formula errors in these controls (six pre-existing Welcome-screen delegation warnings remain). Saved and published the Canvas app. In Firefox as Darwisyah, the Department picker displayed `DEV TEST – Department A/B`, the Cost Centre picker displayed `DEV TEST – Cost Centre A/B`, and selections appeared by name. Cancelled the blank form without submitting a leave request.
- The Superior app's Approved Leave view loaded in Firefox with Department and Cost Centre columns (15 rows for Darwisyah's combined-role account). A single-role Superior access/scope check remains outstanding.

## Remaining validation

- Test selected Department and Cost Centre assignment end to end with a genuine leave request after HR establishes real values. Existing leave records have blank classifications and will remain visible only with no classification filter.
- Verify live Superior dashboard scope using an account with only the Superior role if one becomes available.

## Validation already completed

- `dotnet build` for Leave.Plugins Release: 0 errors.
- `dotnet build` for Leave.DataverseTool: 0 errors. Existing package vulnerability warnings remain.
- Dashboard embedded JavaScript syntax check: passed.

The carry-forward policy and existing leave balances were not modified in this enhancement.
