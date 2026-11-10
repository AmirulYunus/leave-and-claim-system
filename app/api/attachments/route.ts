import { authenticate } from "../../lib/auth";
import { database } from "../../lib/prisma";
import { removeFile, storeFile } from "../../lib/storage";
import {
  attachmentType,
  maxAttachmentBytes,
  maxAttachmentsPerRequest,
  safeFileName,
} from "../../lib/attachment-rules";

export const runtime = "nodejs";

const fail = (error: string, status = 400) =>
  Response.json({ error }, { status });

class Refusal extends Error {
  constructor(
    message: string,
    readonly status = 400,
  ) {
    super(message);
  }
}

// Attach a supporting document to the caller's own pending request. HR checks
// the document when deciding; a saved file alone is not proof of validity.
export async function POST(request: Request) {
  let key = "";
  try {
    const actor = await authenticate(request);
    if (actor instanceof Response) return actor;
    const form = await request.formData().catch(() => null);
    const requestId = Number(form?.get("request_id"));
    const file = form?.get("file");
    if (!Number.isSafeInteger(requestId) || !(file instanceof File))
      return fail("Choose a request and a file to upload.");
    if (file.size <= 0 || file.size > maxAttachmentBytes)
      return fail("Files must be larger than 0 bytes and at most 10 MB.");
    const bytes = new Uint8Array(await file.arrayBuffer());
    const contentType = attachmentType(bytes, file.name);
    if (!contentType) return fail("Upload a PDF, PNG or JPEG file.");
    key = await storeFile(bytes);
    const saved = await database().$transaction(async (tx) => {
      // Only the owner may upload, so the caller's lock is the request owner's.
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(${actor.id}::integer)`;
      const target = await tx.requests.findUnique({
        where: { id: requestId },
        include: { _count: { select: { attachments: true } } },
      });
      if (!target || target.employee_id !== actor.id)
        throw new Refusal("Request not found.", 404);
      if (target.status !== "Pending")
        throw new Refusal(
          "Documents can be added only while the request is pending.",
        );
      if (target._count.attachments >= maxAttachmentsPerRequest)
        throw new Refusal("A request can have at most 10 documents.");
      return tx.attachments.create({
        data: {
          request_id: requestId,
          file_name: safeFileName(file.name),
          content_type: contentType,
          size_bytes: bytes.length,
          storage_key: key,
          uploaded_by: actor.id,
        },
      });
    });
    return Response.json({ id: saved.id }, { status: 201 });
  } catch (error) {
    if (key) await removeFile(key).catch(() => {});
    if (error instanceof Refusal) return fail(error.message, error.status);
    return fail(
      "Could not save the document. Check storage and the database.",
      503,
    );
  }
}
