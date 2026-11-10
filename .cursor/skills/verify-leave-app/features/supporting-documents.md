# Supporting documents

An employee attaches a PDF, PNG or JPEG (up to 10 MB) to their own pending leave request, in the request dialog or from the request list. The owner and HR or Admin can download it; anyone else gets "not found".

## Sub-features

- `doc-attach` uploads a file to a pending leave request (`Attach document`).
- `doc-dialog` uploads with a new request (`Supporting document (PDF, PNG or JPEG, up to 10 MB)`).
- `doc-access` lets the owner and HR/Admin download; others get 404.

## How to get to it (user POV)

- `My leave` or `Dashboard`, `Attach document` on a pending leave row.
- The file field in the leave dialog.
- The file-name link on the row downloads the document.

## Driving it with cursor-ide-browser

Preconditions:

- A pending leave request owned by the signed-in employee (see [Leave request and approval](./leave-request-approval.md)).

- **Attach.** `DOM.setFileInputFiles` is denied, so set the file through the page. With Runtime.evaluate (`awaitPromise: true`), find the row (`tr`) whose text contains the request's reason, take its `input[type=file]`, then assign a `DataTransfer` file and dispatch a bubbling `change`:
  `const f = new File([new Uint8Array([0x25,0x50,0x44,0x46,0x2d,0x31,0x2e,0x34,0x0a])], 'note.pdf', {type: 'application/pdf'}); const dt = new DataTransfer(); dt.items.add(f); input.files = dt.files; input.dispatchEvent(new Event('change', {bubbles: true}));`
  `✓ Document attached.` appears and the row shows link `note.pdf` (`/api/attachments/<id>`).
- **Access.** Runtime.evaluate `fetch('/api/attachments/<id>').then(r => r.status)` returns 200 as the owner and as HR, and 404 (`{"error":"Document not found."}`) as another employee.
- **Proof.** `verify sql "SELECT request_id, file_name, content_type, size_bytes FROM attachments"` returns `note.pdf  application/pdf  9`.

## Gotchas

- The file's leading bytes must match its extension (`%PDF`, the PNG signature, or `FF D8 FF`); a renamed file is rejected.
- Upload works only on the owner's own Pending leave. Claims have no upload.
- The dialog field has no change handler; the file is read from the form on submit. If that upload fails, the leave request is still saved.
