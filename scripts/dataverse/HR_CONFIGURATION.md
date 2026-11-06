# HR notification configuration

Updated 11 October 2026. Configuration is live in the development solution **Leave and claim hub**.

## Change recipients

Open Solutions → Leave and claim hub → Environment variables → **HR Notification Emails** → Current Value. Enter email addresses separated by semicolons, then save. Current development value:

`KhorLord.Yong@Asteriskdigital.com.my;Jennie.Cheong@Asteriskdigital.com.my`

Schema: `cr2c9_HRNotificationEmails`; Text; definition ID `8bba4b77-56c4-f111-a05a-000d3a826b69`. No default value. Export value is No: configure the Current Value separately during target-environment import. At deployment this variable controls notification routing only. Yong Khor Lord and Jennie Cheong are the two confirmed intended HR members. A live Dataverse inspection on 11 October 2026 found no `systemuser` for either configured address, so no HR app share, scoped role assignment or ordinary-role verification can yet be completed for them. IT must make their tenant identities available, then each active Dataverse user and immutable Entra ID must be verified before granting HR access. Admin remains separate. Future configuration design must make the access effect clear if this list also drives HR membership.

**Notify HR after superior leave approval** (`2e7647c4-cdad-6d4d-b2d6-7efe36189eb9`) now uses this environment-variable parameter instead of the temporary developer recipient. Its existing trigger and connection remain unchanged. It has been added to Leaveandclaimhub. The saved flow and exact Dataverse current value were verified; no delivery test or approval notification was sent.

For the 11 October annual-accounting live checks, the enabled flow gained a
trigger condition excluding only the dedicated fictional Employee
`6dd608e3-45c5-4b8b-93a2-5eec5758f62b`. The status filter and configured HR
recipients remain active for other employees. The tests approved fictional
requests without sending HR test emails. Ordinary approval email delivery and
retry behavior still need verification before rollout.

HR maintains existing synced Employee employment details only; account creation
stays with IT. HR employee creation is removed from intended scope, but native
New commands and creation permissions have not yet been changed. The shared
absence calendar is now a requested approval follow-up, per the 15 October
clarification in `REVISED_LEAVE_WORKFLOWS.md`. HR acknowledgement is not
required and workflow notifications are email only.

Microsoft documentation: https://learn.microsoft.com/en-us/power-automate/environment-variables

Evidence: `hr-notification-emails-configured.png`, `hr-notification-flow-configured.png`.

## User clarification —10October2026

HR acknowledgement is notification only; no separate acknowledgement action
or queue is required. All workflow notifications are email only. A shared
absence calendar visible to everyone is requested, preferably in Teams; it
remains undeployed. Existing HR email delivery to the new recipients is untested.
