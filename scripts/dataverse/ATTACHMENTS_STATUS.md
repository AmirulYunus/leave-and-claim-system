# Review attachments — 10 October 2026

Published a native Notes timeline on the shared Leave Request Information form
f297bf4c-1a1d-4560-b594-572612a10d5c. HR leave operations and Superior review
workspace use this form. LR-0007 (c307e48e-24c2-f111-aaad-000d3a826b69) now
shows its existing LEAVE_POLICY.md file in both published runtimes.

Employee Canvas attachments are stored as Dataverse Notes (annotation),
not in the separate cr2c9_supportingdocument File column or legacy Attachment
text column. Its note is c407e48e-24c2-f111-aaad-000d3a826b69, isDocument true,
MIME application/octet-stream, size 7,335 bytes. An SDK read decoded the payload
in memory and verified that its size matched the metadata. No record backups
were made. The in-app browser did not report a download event, so the same
action was checked in Chrome. Chrome Recent Download History confirmed
LEAVE_POLICY (4).md, 7.2 KB, Done after clicking the attachment in the HR app.
This was a download verification, not a data backup. Separate ordinary-role
access remains unverified.

The timeline expands records by default, hides existing-note Edit, Delete and
Pin actions, and disables Timeline Highlights and the changed-record summary.
Native note creation commands remain and follow existing Dataverse privileges.
No security roles or sharing permissions were changed. Existing form scripts,
decision checks, related date/history grids and the self-approval block were
preserved. The editor was exited after successful save/publish.

Evidence: attachments-hr-live.png, attachments-superior-live.png,
attachment-download-chrome.png.
The development account could see the same attachment in both apps. This does
not establish HR/Superior record-level access for other tenant accounts.

## Reported employee network error

The screenshot shows Invalid data from the network in a read-only Studio
preview. The published employee player opened LR-0007 and displayed the
attachment without that banner; captured player logs showed only icon
registration warnings. Older HR/Superior tabs showed server-unreachable/offline
notices, which cleared after reload. Those observations do not establish the
cause of the separate Canvas message. A new Studio tab opened but diagnostic
controls could not be operated through the in-app browser; it was closed
without publishing changes. No banner suppression or speculative formula/data
change was applied. A native Chrome Live monitor session remained at
Connecting to session with zero events and was closed. No usable failed-call
trace was captured. Evidence: employee-request-no-network-error.png.

Use a Live monitor trace of a reproduced failure to identify the exact data
source, operation, status and response. The stale-session/connectivity
explanation is only a possibility, not a verified root cause.

Microsoft references:
- https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/set-up-timeline-control
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/attachment-annotation-files
- https://learn.microsoft.com/en-us/power-apps/maker/monitor-overview
