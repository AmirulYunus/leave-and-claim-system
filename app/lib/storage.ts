import { randomUUID } from "node:crypto";
import { mkdir, readFile, unlink, writeFile } from "node:fs/promises";
import path from "node:path";

// Supporting-document storage. Files are stored under random keys, never under
// the uploaded name. Development uses local disk (UPLOAD_DIR, default
// .storage/uploads); staging and production will use S3 through this module.
function directory() {
  const driver = process.env.STORAGE_DRIVER ?? "local";
  if (driver !== "local")
    throw new Error(`Storage driver "${driver}" is not implemented yet.`);
  return path.resolve(process.env.UPLOAD_DIR ?? ".storage/uploads");
}

function location(key: string) {
  if (!/^[0-9a-f-]{36}$/.test(key)) throw new Error("Invalid storage key.");
  return path.join(directory(), key);
}

export async function storeFile(bytes: Uint8Array) {
  const key = randomUUID();
  await mkdir(directory(), { recursive: true });
  await writeFile(location(key), bytes, { flag: "wx" });
  return key;
}

export function loadFile(key: string) {
  return readFile(location(key));
}

export async function removeFile(key: string) {
  await unlink(location(key)).catch((error: NodeJS.ErrnoException) => {
    if (error.code !== "ENOENT") throw error;
  });
}
