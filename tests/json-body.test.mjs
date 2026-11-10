import test from "node:test";
import assert from "node:assert/strict";
import { readJsonObject } from "../app/lib/json-body.ts";

function post(body) {
  return new Request("http://localhost/api/workspace", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body,
  });
}

test("a request body must be a JSON object", async () => {
  const parsed = await readJsonObject(post('{"kind":"Leave","hours":8}'));
  assert.equal(parsed instanceof Response, false);
  assert.deepEqual(parsed, { kind: "Leave", hours: 8 });
  assert.deepEqual(await readJsonObject(post("{}")), {});

  for (const body of ["{", "null", "[]", "1", '"no"', "true", "false", ""]) {
    const result = await readJsonObject(post(body));
    assert.ok(result instanceof Response, body);
    assert.equal(result.status, 400, body);
    assert.deepEqual(await result.json(), { error: "Send a JSON object." });
  }
});
