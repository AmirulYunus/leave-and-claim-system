# Ordinary-role rollout verification

Status: ordinary Employee API access partly verified with Darwisyah Fakhira;
published-app verification with her own sign-in remains. A separate ordinary
Superior and active HR Dataverse users are still needed. The developer account
has Basic User and System Administrator. A live inspection on
11 October 2026 found 152 active `systemuser` rows, only three with an Entra
object ID, and no matching `systemuser` for either confirmed HR member:
`KhorLord.Yong@Asteriskdigital.com.my` (Yong Khor Lord) or
`Jennie.Cheong@Asteriskdigital.com.my` (Jennie Cheong).

Darwisyah Fakhira is the user-nominated ordinary Employee test candidate. A
live inspection found one active Employee profile with email
`Darwisyah.Fakhira@Asteriskdigital.com.my` and one enabled Dataverse user whose
Entra object ID matches that profile. After the user confirmed she does not
need Maker access, her **Environment Maker** direct role was removed. She now has
**Basic User** and **Leave Employee (development test)** only as direct roles.
Her own Employee row (`adae5971-d3c2-f111-a05a-000d3a826b69`) was assigned
to her; no other Employee owner was changed. On 11 October 2026, Dataverse
SDK calls impersonating Darwisyah could read her own Employee row and the
shared Annual Leave type, but were denied another employee's Employee row,
Leave Request, saved request date, balance period, balance ledger entry,
decision-history row and Note attachment. Dataverse's effective-rights check
gave her no Write, Delete, Assign or Share right on her own Employee profile.
This is an API-level check, not proof that the published Canvas app works with
her own sign-in. An impersonated SDK call subsequently created and read back
one fictional own Pending Superior request (`ab8f08a4-4cc5-f111-a05a-000d3a826b69`)
and one linked 4-hour date (`ac8f08a4-4cc5-f111-a05a-000d3a826b69`). The
request is still pending; no approval or balance deduction was attempted.
The development-test role is not approved as a production
security design; attachment write/delete paths,
indirect/team roles and published UI behavior still need verification.

**Employee self service** was shared with Darwisyah as **User** in the
Developer environment after the user approved that specific grant. Power Apps
Manage access shows her as User with the Leave Employee (development test) data
access role; [share evidence](employee-test-app-share.jpg). She was not given
Co-owner, HR, Superior, Environment Maker or System Administrator access.
The published app gained **My leave balance** on 13 October 2026. Its Canvas
formula selects a period using the signed-in Entra object ID, then totals that
period's ledger entries. Studio Preview under the developer administrator
account is only a calculation check; Darwisyah must open the published app with
her own sign-in to verify this page and its Dataverse permissions. Record the
period shown, compare the grant/debit/reversal entries with the displayed days,
then try a foreign period through a direct API route. Do not treat a hidden
Canvas menu or filtered label as a Dataverse security test.
The current grant plug-in assigns a new period to the Employee row's owner, and
grant, approved-debit and reversal entries inherit that period owner. Thus a
Basic-depth Employee role can read its own balance only when Employee ownership
is linked to the verified sign-in account (or an equivalent secure access
mechanism is established). Darwisyah's Employee row is assigned to her, but an
own balance period and positive own-period read have not yet been verified.
Most other development Employee rows remain owned by the maker, so their
balances are not ready for ordinary-account access testing or production.
The HR email environment variable is a mail recipient setting, not an app
share, role assignment or Dataverse permission.
Both fictional Employee fixture rows remain owned by the developer account.

IT provisions the sign-in accounts. Before production rollout, identify one
ordinary employee, a different employee who is that person's latest synced
Superior, and an HR member with the intended maintenance permissions. Keep
the administrator account separate. Confirm each user's active Dataverse
`systemuser` and Entra object ID, then record the app shares and table access
assigned to each account. Do not use the fictional fixture's temporary
identity swap to stand in for this test.

These role tests can be run in the current Developer environment before
production. Microsoft permits adding coworkers manually and sharing apps there
for development/testing. IT first provides separate tenant sign-in account(s)
with appropriate Power Apps licensing; the maker/admin then adds each account
to this environment, assigns only the intended test role and shares the
matching app. An Employee-table row alone is not a sign-in user. Use dummy leave
records and separate browser sign-ins. Do not give the test account System
Administrator or Environment Maker merely to make the app open. A separate
ordinary Employee test account is the first useful case; a different Superior
and an HR identity are still needed for their respective positive tests.

14 October 2026 profile-write check: with the SDK impersonating Darwisyah's
ordinary Employee account, updates to her own Employee Level, Entra User ID,
Superior lookup and Email were each denied. The four fields were read again
under the administrator account and were unchanged. This confirms the current
ordinary Employee role cannot maintain even its own Employee profile through
this SDK path. The existing synchronous employment input guard additionally
requires organization-wide Employee maintenance privilege for the protected
employment/identity/Superior fields. This does not yet prove the planned
Superior role or all alternative write routes.

The user confirmed the intended Superior-app boundary on 14 October:
all Employee profiles are view-only for a Superior; Leave Requests and Leave
Cancellations must be limited to staff whose latest synced Superior lookup
points to that signed-in Superior. The form's existing server decision guards
already check that assignment when a decision is saved. The dedicated **Leave
Superior** role now exists in the Developer environment, is in the solution,
and is associated with **Superior review workspace**. Its Employee privilege
is Global Read only; it has no Employee Write and no legacy Leave Approval
privilege. It has Basic Read/Write for requests and cancellations and Basic Read
for their evidence. **No user is assigned this role.** The stale legacy Leave
Approvals navigation page was removed; the app was saved and published with
Employees, Leave Requests and Leave Cancellations navigation.

15 October Developer-environment update: the private Superior access-grant
table, its lookup relationships, and six synchronous sharing steps are deployed.
The plug-in shares a request and cancellation with the current active synced
Superior, shares dates and decisions read-only, and reconciles old/new grants
when the Employee Superior lookup is updated. It tracks grants it created so a
manager change does not silently revoke an unrelated manual share. The Request
to Note relationship cascades share/unshare; the Date, Decision and
Cancellation relationships do not, so those children receive direct grants.
The **Leave Superior** role remains unassigned to any user.

15 October read-only check: Muhammad Amirul's Employee row points to **Yong
Khor Lord** as its current Superior, and Yong's Employee row has an Entra object
ID. The Developer environment still has **zero** `systemuser` records matching
that Entra ID or `KhorLord.Yong@Asteriskdigital.com.my`. No Yong app share or
role assignment can be verified until IT makes his tenant account available as
an enabled Dataverse user in this environment. The manager lookup itself does
not grant him app or table access.

The Superior app now contains only **Assigned pending leave requests** and
**Assigned pending cancellations** as its Request/Cancellation views. Both
filter pending status and join the access-grant record for the signed-in user;
the generic Active views were removed from this app. A fictional employee's
same-value Superior update backfilled three requests and eight grant markers;
repeating it was idempotent. A new fictional pending request appeared as one
row in the assigned pending view after reconciliation. The fixture temporarily
swaps the maker's Entra ID to submit, which prevents the Create-time sharing
step from resolving that Superior; its identity is restored and the same-value
Superior update reconciles the grant. This is not a live ordinary-account test
of automatic sharing on a normal submission.

Before assigning the role or sharing the app, audit/backfill existing real
assigned-staff records and verify the positive and negative paths with a
separate ordinary Superior sign-in and direct Dataverse/API calls. In particular,
test current-manager access, unrelated and own-request denials, manager change,
evidence, decisions, cancellation, and direct API access. Do not grant Employee
Write merely to make the form open.

Yong Khor Lord and Jennie Cheong are the two designated HR members. Once IT
makes their tenant identities available in the target environment, verify each
active Dataverse user and immutable Entra object ID before assigning a scoped HR
role and access to **HR leave operations**. The HR role should maintain existing
Employee employment inputs and inspect leave evidence/balances needed for HR
work. It must not grant System Administrator, account provisioning, sync-owned
Entra identity/Superior changes, or the held HR approval override. Recheck
their app/table permissions with each person's own sign-in. No HR access grant
has yet been made in this development environment.

| Signed-in account | Expected access test |
| --- | --- |
| Employee | Open only own requests, dates, attachments and balances; submit an own future approved-leave cancellation; cannot view or decide another employee's private request or edit policy/sync fields. |
| Superior | Open assigned staff's pending request and evidence; decide it once through the published Superior form; decide an assigned staff member's pending future cancellation; cannot decide own or unrelated requests. |
| HR | Receive configured emails and maintain authorized employment inputs after IT establishes verified identity and Dataverse permissions; email configuration alone gives no access. |
| Admin/IT | Verify deployment and role configuration separately; the unused System administration app is outside the current employee rollout. |

Run the positive paths with separate browser sessions. For each denial, check
both the app and a direct Dataverse/API route so menu hiding is not mistaken
for access control. Include manager reassignment after sync, duplicate decision,
insufficient balance, direct ledger writes, supporting-file access, and an
employee attempting to edit identity/Superior fields. Inspect decision history
and ledger after every approval or cancellation. A successful administrator
test does not close these checks.

Completed development checks: published Superior approval on fictional LR-0022
wrote one decision and one −8h debit (128→120h). A published Superior
cancellation decision on that request wrote one linked +8h reversal and changed
the original to Cancelled (120→128h). A separate mixed 4+8h SDK cancellation
restored exactly 12h once. These used the developer administrator account.
