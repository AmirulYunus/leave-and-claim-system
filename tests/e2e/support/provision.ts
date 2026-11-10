import {
  request as playwrightRequest,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import { freshJoin } from "./rules";

export const temporaryPassword = "e2e-temp-password";
export const replacementPassword = "e2e-new-password-1";
export const mismatchedPassword = "e2e-new-password-2";
export const wrongPassword = "e2e-wrong-password";

export type CreatedEmployee = {
  id: number;
  name: string;
  email: string;
  temporaryPassword: string;
  password: string;
};

type HrAccount = { baseURL: string; email: string; password: string };

async function withHrApi(
  source: Page | HrAccount,
  run: (hrApi: APIRequestContext, baseURL: string) => Promise<CreatedEmployee>,
) {
  if ("request" in source) {
    return run(source.request, new URL(source.url()).origin);
  }
  const hrApi = await playwrightRequest.newContext({
    baseURL: source.baseURL,
    extraHTTPHeaders: { Origin: source.baseURL },
  });
  try {
    const signedIn = await hrApi.post("/api/auth/session", {
      data: { email: source.email, password: source.password },
    });
    if (!signedIn.ok())
      throw new Error(`HR sign-in failed: ${await signedIn.text()}`);
    return await run(hrApi, source.baseURL);
  } finally {
    await hrApi.dispose();
  }
}

export async function createTemporaryEmployee(
  source: Page | HrAccount,
  tag: string,
  options?: { changePassword?: boolean },
): Promise<CreatedEmployee> {
  return withHrApi(source, async (hrApi, baseURL) => {
    const email = `e2e.${tag}@example.test`;
    const name = `E2E ${tag}`;
    const password = options?.changePassword
      ? `New-${tag}-e2e-pw`
      : temporaryPassword;
    const response = await hrApi.post("/api/employees", {
      data: {
        name,
        email,
        role: "Employee",
        join_date: freshJoin,
        temporary_password: temporaryPassword,
        employment_type: "Full-time",
        employee_level: 1,
        probation_completed_on: "2024-04-15",
        is_active: true,
      },
    });
    if (!response.ok()) {
      throw new Error(
        `Creating ${email} failed: ${response.status()} ${await response.text()}`,
      );
    }
    const listed = await hrApi.get("/api/employees");
    if (!listed.ok())
      throw new Error(`Reading employees failed: ${await listed.text()}`);
    const body = (await listed.json()) as {
      employees?: { id: number; email: string }[];
    };
    const row = body.employees?.find((employee) => employee.email === email);
    if (!row) throw new Error(`The new employee ${email} was not saved.`);
    if (options?.changePassword) {
      const employeeApi = await playwrightRequest.newContext({
        baseURL,
        extraHTTPHeaders: { Origin: baseURL },
      });
      try {
        const session = await employeeApi.post("/api/auth/session", {
          data: { email, password: temporaryPassword },
        });
        if (!session.ok())
          throw new Error(`Temporary sign-in failed: ${await session.text()}`);
        const changed = await employeeApi.post("/api/auth/password", {
          data: {
            current_password: temporaryPassword,
            new_password: password,
          },
        });
        if (!changed.ok())
          throw new Error(`Password change failed: ${await changed.text()}`);
      } finally {
        await employeeApi.dispose();
      }
    }
    return { id: row.id, name, email, temporaryPassword, password };
  });
}
