"use client";

import { FormEvent, useState } from "react";
import { passwordLength } from "./lib/password-rules";

// Sign-in and first-sign-in password change. Both reload the page on success
// so the workspace starts from a clean state for the signed-in employee.
export function AuthScreen({
  mode,
  problem,
}: {
  mode: "checking" | "signedOut" | "changePassword";
  problem: string;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function send(url: string, method: string, body?: object) {
    const response = await fetch(url, {
      method,
      headers: { "Content-Type": "application/json" },
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error);
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const form = new FormData(event.currentTarget);
    if (
      mode === "changePassword" &&
      form.get("new_password") !== form.get("confirm_password")
    ) {
      setError("The new passwords do not match.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      if (mode === "signedOut")
        await send("/api/auth/session", "POST", {
          email: form.get("email"),
          password: form.get("password"),
        });
      else
        await send("/api/auth/password", "POST", {
          current_password: form.get("current_password"),
          new_password: form.get("new_password"),
        });
      window.location.reload();
    } catch (error) {
      setError(error instanceof Error ? error.message : "Please try again.");
      setBusy(false);
    }
  }
  async function signOut() {
    setBusy(true);
    await send("/api/auth/session", "DELETE").catch(() => {});
    window.location.reload();
  }

  return (
    <main className="auth-screen">
      <section className="modal" aria-labelledby="auth-title">
        <div className="modal-header">
          <h2 id="auth-title">
            {mode === "changePassword"
              ? "Choose a new password"
              : "Sign in to People & Co."}
          </h2>
        </div>
        {mode === "checking" ? (
          <p className="modal-intro">{problem || "Loading…"}</p>
        ) : (
          <form onSubmit={submit}>
            <p className="modal-intro">
              {mode === "changePassword"
                ? "Your account has a temporary password. Choose your own to continue."
                : "Use the email address and password from HR."}
            </p>
            {mode === "signedOut" ? (
              <>
                <label>
                  Email
                  <input
                    autoFocus
                    name="email"
                    type="email"
                    autoComplete="username"
                    maxLength={254}
                    required
                  />
                </label>
                <label>
                  Password
                  <input
                    name="password"
                    type="password"
                    autoComplete="current-password"
                    maxLength={passwordLength.max}
                    required
                  />
                </label>
              </>
            ) : (
              <>
                <label>
                  Temporary password
                  <input
                    autoFocus
                    name="current_password"
                    type="password"
                    autoComplete="current-password"
                    maxLength={passwordLength.max}
                    required
                  />
                </label>
                {["new_password", "confirm_password"].map((name) => (
                  <label key={name}>
                    {name === "new_password"
                      ? "New password"
                      : "Confirm new password"}
                    <input
                      name={name}
                      type="password"
                      autoComplete="new-password"
                      minLength={passwordLength.min}
                      maxLength={passwordLength.max}
                      required
                    />
                  </label>
                ))}
                <p className="field-help">
                  Use {passwordLength.min} to {passwordLength.max} characters.
                </p>
              </>
            )}
            {(error || problem) && (
              <p className="form-error" role="alert">
                {error || problem}
              </p>
            )}
            <div className="form-actions">
              {mode === "changePassword" && (
                <button
                  type="button"
                  className="button secondary"
                  disabled={busy}
                  onClick={signOut}
                >
                  Sign out
                </button>
              )}
              <button className="button primary" disabled={busy}>
                {busy
                  ? "Please wait…"
                  : mode === "changePassword"
                    ? "Save password"
                    : "Sign in"}
              </button>
            </div>
          </form>
        )}
      </section>
    </main>
  );
}
