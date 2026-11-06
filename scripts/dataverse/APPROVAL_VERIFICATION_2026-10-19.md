# Developer approval verification — 19 October 2026

The checks below use the Developer environment and fictional Employee profile `a3a2b544-41de-4385-bacf-9859c45656c5`. The HR notification flow explicitly excludes that profile, so this test sent no HR decision email. No real employee leave request was approved.

- Jennie Cheong and Yong Khor Lord each have active Entra-backed Dataverse users with `Leave HR` and `Leave Superior` roles. Both roles include organization-wide App Module Read and are associated with their respective active model-driven apps. Dataverse reads of both app modules while impersonating each user succeeded. This verifies the server-side access configuration, not a browser session signed in by Jennie or Yong.
- A new fictional Annual Leave request, `7fbe43c8-6ea1-4aa6-97fa-d55746757eb6`, was submitted pending with one eight-hour date and an available annual balance of 128 hours. A Dataverse decision performed as Jennie's user identity approved it. The saved request has Jennie's Entra decision identity and timestamp, one approved decision history row, and exactly one `-8`-hour Annual Leave ledger entry. The fictional available balance became 120 hours.
- A second approval attempt as Jennie was rejected. The request retained one decision and one debit, and the fictional balance remained 120 hours.
- Darwisyah Fakhira's Employee profile and Entra user still match and are active; her only direct Dataverse role is `Leave Employee (development test)`.

The remaining user-session check is for Jennie and Yong to open their published HR/Superior apps with their own work accounts and confirm a pending request appears and can be decided. Do not infer that a successful Dataverse impersonation proves browser sign-in or a Superior-only role path. A live ordinary-role request and attachment check from Darwisyah's own session also remains required. Run those checks on a clearly identified fictional request before approving anything.
