# Leave enhancement review — 6 November 2026

Developer environment. “Call Centre” in the request list means **Department**; there is no separate Call Centre field. Darwisyah submitted LR-0128 for 24 November 2026 to verify the classification fields. It remains Pending Superior and was not approved.

| Enhancement | Status and evidence |
| --- | --- |
| Employee, Superior and HR dashboards | Employee balance dashboard is published. Superior and HR dashboard resource has pending, approved, upcoming and calendar sections; their Employee record forms provide individual balances. A team-wide balance summary is not currently on the Superior/HR landing dashboard. |
| Date, Department and Cost Centre calendar filters | Published for the Employee calendar and the Superior/HR dashboard. Superior dashboard uses assigned-grant scope; HR dashboard is company-wide. |
| All people on leave in calendar | Removed the three-person-per-day truncation in `cr2c9_roleLeaveDashboard.html`. Each day now shows a unique-person count and a scrollable list of every person with approved leave. Calendar data is fetched for the complete displayed month even when the report date range starts mid-month. Published and live HR dashboard loaded November data. Employee Canvas calendar already uses `Concat(Filter(colCalendarAbsences,...))` without a three-person cap. |
| Approved Leave views | Published in both HR and Superior apps. Superior view uses assigned-grant scope; HR view has company-wide scope. The dashboard's approved table now renders every matching row rather than the first ten. Published and live HR dashboard showed eight November records. |
| Newest applications / upcoming order | HR and Superior saved views sort applications by `createdon` descending and upcoming leave by start date ascending. Employee Canvas `RecordsGallery3` was changed to sort by `createdon` descending, saved and published. Darwisyah's live list displayed LR-0127, 0126, 0125, 0124 in order. |
| Department and Cost Centre columns/filters | Both columns are in HR/Superior request views. HR maintains option tables. Employee request and calendar pickers are published. LR-0128 saved both selected values, verified in the HR All Leave Requests view. The Employee request detail pane shows these saved values as blank and needs a display fix. |
| Summary, Pending, Approved and Upcoming views | Published in Superior; HR has All, Pending, Approved and Upcoming. “Assigned pending leave requests” was renamed “Summary Leave Request.” |
| Emergency notice / Training Category | Server accepts retrospective Emergency requests and requires evidence for approval. Superior form script only shows Training Category for Training Leave. No 3-day advance restriction applies to Emergency Leave. |
| Medical name | Published display name is Medical Leave. |
| HR monthly / quarterly / yearly export | Live HR report showed 8 November, 15 Q4 and 15 annual approved requests; CSV download completed at 898 bytes for November. Period selector shows only the relevant Month or Quarter field. |

## Remaining gaps

- Superior and HR landing dashboards do not yet show individual or team leave-balance figures. Showing an aggregate would need a defined meaning and access to the underlying balance ledger; recorded balances can include unearned historical credit. The existing Employee balance view and HR Employee annual-balance form remain the sources for balance review.
- LR-0128 confirms Department/Cost Centre propagation to the stored request and HR view. The Employee request detail pane still renders both fields blank; this display defect remains open.
