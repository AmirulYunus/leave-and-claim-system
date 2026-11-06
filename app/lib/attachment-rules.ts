// Supporting documents: PDF, PNG or JPEG up to 10 MB. The file's leading bytes
// must match its extension so a renamed file cannot pass as another type.
export const maxAttachmentBytes = 10 * 1024 * 1024;
export const maxAttachmentsPerRequest = 10;

const formats = [
  { type: "application/pdf", magic: [0x25, 0x50, 0x44, 0x46], ext: [".pdf"] },
  {
    type: "image/png",
    magic: [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a],
    ext: [".png"],
  },
  { type: "image/jpeg", magic: [0xff, 0xd8, 0xff], ext: [".jpg", ".jpeg"] },
];

// The content type to store, or "" when the file is not an accepted format.
export function attachmentType(bytes: Uint8Array, fileName: string) {
  const name = fileName.toLowerCase();
  const format = formats.find((candidate) =>
    candidate.ext.some((ext) => name.endsWith(ext)),
  );
  if (!format || bytes.length < format.magic.length) return "";
  return format.magic.every((byte, index) => bytes[index] === byte)
    ? format.type
    : "";
}

// A display name without directories, control characters or quotes, safe to
// echo back in a Content-Disposition header.
export function safeFileName(fileName: string) {
  const base = fileName.split(/[\\/]/).pop() ?? "";
  // eslint-disable-next-line no-control-regex
  const cleaned = base.replace(/[\u0000-\u001f\u007f"]/g, "").trim();
  return cleaned.slice(-200) || "document";
}
