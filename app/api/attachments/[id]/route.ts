import { authenticate } from "../../../lib/auth";
import { hasPermission } from "../../../lib/permissions";
import { database } from "../../../lib/prisma";
import { loadFile } from "../../../lib/storage";

export const runtime = "nodejs";

// Download a supporting document. The request's owner and reviewers can read
// it; anyone else gets the same response as a missing file.
export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  try {
    const actor = await authenticate(request);
    if (actor instanceof Response) return actor;
    const id = Number((await params).id);
    const attachment = Number.isSafeInteger(id)
      ? await database().attachments.findUnique({
          where: { id },
          include: { request: { select: { employee_id: true } } },
        })
      : null;
    if (
      !attachment ||
      (attachment.request.employee_id !== actor.id &&
        !(await hasPermission(actor.id, "request.viewAll")))
    )
      return Response.json({ error: "Document not found." }, { status: 404 });
    const bytes = await loadFile(attachment.storage_key);
    return new Response(new Uint8Array(bytes), {
      headers: {
        "Content-Type": attachment.content_type,
        "Content-Disposition": `attachment; filename*=UTF-8''${encodeURIComponent(attachment.file_name)}`,
        "Content-Length": String(bytes.length),
        "Cache-Control": "private, no-store",
        "X-Content-Type-Options": "nosniff",
      },
    });
  } catch {
    return Response.json(
      { error: "Could not read the document. Check storage." },
      { status: 503 },
    );
  }
}
