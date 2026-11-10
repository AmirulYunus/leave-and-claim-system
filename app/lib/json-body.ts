const invalidJson = () =>
  Response.json({ error: "Send a JSON object." }, { status: 400 });

type JsonValue = Awaited<ReturnType<Request["json"]>>;
type JsonObject = { [key: string]: JsonValue };

function isJsonObject(value: unknown): value is JsonObject {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export async function readJsonObject(
  request: Request,
): Promise<JsonObject | Response> {
  try {
    const body: unknown = await request.json();
    if (!isJsonObject(body)) return invalidJson();
    return body;
  } catch {
    return invalidJson();
  }
}
