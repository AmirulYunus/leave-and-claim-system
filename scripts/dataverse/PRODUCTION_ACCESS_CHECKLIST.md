# Production access and deployment reminder

Use this checklist when the Leave Management solution is ready for production.
This is a plan, not a record of production deployment or access grants. The
current environment is Developer; do not share it as the company production app.

## Before importing or sharing

- [ ] Finish scoped Employee, Superior and HR Dataverse roles, server access
  checks, and ordinary-account verification. Hiding an app menu is insufficient.
- [ ] Audit/backfill existing assigned-staff records and verify the deployed
  Leave Superior sharing steps, manager-change reconciliation, and filtered
  pending request/cancellation views using an ordinary Superior sign-in and
  direct API calls. The Developer role and app views are deployed; Yong and
  Jennie have role assignments, but their own-session app tests are pending.
- [ ] Verify employee-only access to own requests, attachments and balances;
  Superior decisions for assigned staff only; HR maintenance of existing
  employment inputs and review data. Verify the newly specified HR approval
  path with direct Leave HR role membership and no self-approval; email
  notification recipients alone must not grant approval authority.
- [ ] Use separate ordinary Employee, Superior and HR sign-ins to test both app
  screens and direct Dataverse/API access in the Developer environment before
  production. IT supplies real tenant test identities; add them manually to
  the Developer environment with scoped roles. Resolve open findings in
  `ACCESS_VERIFICATION.md` before rollout.
- [ ] Resolve Employee-row ownership and record access. The development sync
  currently leaves Employee rows owned by Muhammad Amirul, except Darwisyah's
  row which was assigned to her for the ordinary-user test. Ownership alone
  therefore cannot enforce each employee's own-row access. After IT provisions
  verified Dataverse users, link each Employee profile to its sign-in identity,
  assign appropriate ownership or equivalent secure record access, and test it
  with separate employee accounts. Do not bulk-reassign dummy rows as a proxy.
- [ ] IT prepares a Production-type environment with Dataverse, eligible user
  accounts and Power Apps licensing. Keep a separate IT/admin identity for
  deployment and platform administration.

## Production deployment

- [ ] Import the release solution into Production and configure production
  connection references and environment-variable current values.
- [ ] Set `cr2c9_HRNotificationEmails` to
  `KhorLord.Yong@Asteriskdigital.com.my;Jennie.Cheong@Asteriskdigital.com.my`.
  This controls email delivery only; it is not an access grant.
- [ ] Configure the all-staff calendar recipient
  `all@asteriskdigital.com.my` and IT-approved Microsoft Graph calendar access
  for creating invitations in the approved employee's mailbox. Test the
  distribution list, duplicate prevention and cancellation updates before
  enabling the calendar flow for ordinary approvals.
- [ ] In Power Platform admin center → Manage → Environments → Production →
  Settings → Users + permissions → Users, confirm both HR members are enabled
  Dataverse users with verified tenant Entra identities. IT adds/provisions any
  missing user; an Employee table row alone does not meet this requirement.
- [ ] Assign the scoped Leave Employee role and share Employee self service with
  ordinary staff, including Muhammad Amirul's everyday account. Assign the
  scoped Leave HR role and share HR leave operations with **only** Yong Khor Lord
  and Jennie Cheong. They may also retain Employee access for their own leave.
- [ ] Assign the scoped Leave Superior role and share Superior review workspace
  only with actual managers. A manager also retains Employee access. Do not
  share HR or Superior apps with all employees.
- [ ] Confirm `Leave HR` and `Leave Superior` include **App Module Read** and
  the model-driven runtime's **Entity metadata Read** (`prvReadEntity`), and
  are associated with their respective model-driven apps. After assigning
  roles, run the app access checker for Jennie and Yong, then have each sign in
  and open the app. The access checker alone does not detect missing runtime
  metadata privileges. The Developer roles were corrected on 19 October, but
  the ordinary-user opening test is still pending.
- [ ] Ensure Muhammad Amirul's everyday production account has no System
  Administrator, Environment Maker, HR or Superior role unless separately
  required for a real assigned duty. Use the separate IT/admin identity for
  administration. Check for automatically assigned roles and group memberships.
- [ ] Test as an ordinary employee, each HR member and an assigned Superior.
  Confirm expected access and denials through the published apps and direct
  Dataverse/API calls; verify attachments, balances and decisions.

Microsoft guidance: [add environment users](https://learn.microsoft.com/en-us/power-platform/admin/add-users-to-environment),
[assign roles](https://learn.microsoft.com/en-us/power-platform/admin/assign-security-roles),
[share model-driven apps](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/share-model-driven-app),
[share canvas apps](https://learn.microsoft.com/en-us/power-apps/maker/canvas-apps/share-app).
