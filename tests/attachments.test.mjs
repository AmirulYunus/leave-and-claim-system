import test from "node:test";
import assert from "node:assert/strict";
import { attachmentType, safeFileName } from "../app/lib/attachment-rules.ts";

const bytes = (...values) => new Uint8Array([...values, 0, 0, 0, 0]);
const pdf = bytes(0x25, 0x50, 0x44, 0x46);
const png = bytes(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a);
const jpeg = bytes(0xff, 0xd8, 0xff);

test("accepted formats are detected from their leading bytes", () => {
  assert.equal(attachmentType(pdf, "certificate.PDF"), "application/pdf");
  assert.equal(attachmentType(png, "scan.png"), "image/png");
  assert.equal(attachmentType(jpeg, "photo.jpeg"), "image/jpeg");
  assert.equal(attachmentType(jpeg, "photo.jpg"), "image/jpeg");
});

test("renamed, truncated and unsupported files are rejected", () => {
  assert.equal(attachmentType(png, "renamed.pdf"), "");
  assert.equal(attachmentType(pdf, "evidence.docx"), "");
  assert.equal(attachmentType(new Uint8Array([0x25]), "short.pdf"), "");
  assert.equal(attachmentType(pdf, "no-extension"), "");
});

test("display names lose paths, control characters and quotes", () => {
  assert.equal(safeFileName("C:\\Users\\me\\MC cert.pdf"), "MC cert.pdf");
  assert.equal(safeFileName("../../etc/passwd"), "passwd");
  assert.equal(safeFileName('a"b\r\nc.pdf'), "abc.pdf");
  assert.equal(safeFileName("   "), "document");
  assert.equal(safeFileName(`${"x".repeat(300)}.pdf`).length, 200);
});
