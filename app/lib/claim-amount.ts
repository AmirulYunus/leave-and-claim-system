const claimAmountMessage =
  "Enter a claim amount from RM 0.01 to RM 100,000 with at most two decimal places.";

export function claimAmountProblem(value: unknown): string {
  if (typeof value !== "number" || !Number.isFinite(value))
    return claimAmountMessage;
  const cents = Math.round(value * 100);
  if (
    value <= 0 ||
    value > 100000 ||
    cents < 1 ||
    Math.abs(value * 100 - cents) > 0.000001
  )
    return claimAmountMessage;
  return "";
}
