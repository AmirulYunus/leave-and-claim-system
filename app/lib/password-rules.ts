// Shared by the server and the sign-in screen; keep free of Node imports.
export const passwordLength = { min: 12, max: 128 };

export function passwordProblem(password: unknown) {
  if (
    typeof password !== "string" ||
    password.length < passwordLength.min ||
    password.length > passwordLength.max
  )
    return `Use a password of ${passwordLength.min} to ${passwordLength.max} characters.`;
  return "";
}
