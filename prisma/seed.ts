import { loadEnvConfig } from "@next/env";
import { database } from "../app/lib/prisma";
import { hashPassword } from "../app/lib/password";
import { passwordProblem } from "../app/lib/password-rules";

loadEnvConfig(process.cwd());
const prisma = database();
const profiles = [
  {
    name: "System Admin",
    email: "admin@example.test",
    join_date: new Date("2020-01-01"),
    role: "Admin",
  },
  {
    name: "Amirul",
    email: "amirul@example.test",
    join_date: new Date("2024-01-15"),
    role: "Employee",
  },
  {
    name: "HR",
    email: "hr@example.test",
    join_date: new Date("2020-01-01"),
    role: "HR",
  },
  {
    name: "Sarah Tan",
    email: "sarah@example.test",
    join_date: new Date("2023-03-01"),
    role: "Employee",
  },
  {
    name: "Daniel Lee",
    email: "daniel@example.test",
    join_date: new Date("2022-02-01"),
    role: "Employee",
  },
];
async function main() {
  // Local demo accounts only. Accounts created in the app get a temporary
  // password that must be changed at first sign-in.
  const password = process.env.SEED_PASSWORD ?? "";
  if (passwordProblem(password)) {
    console.error(
      `Set SEED_PASSWORD in .env.local. ${passwordProblem(password)}`,
    );
    process.exitCode = 1;
    return;
  }
  const password_hash = await hashPassword(password);
  // Demo employment details so entitlements can be calculated; never applied
  // over details HR has already entered.
  const employment = { employment_type: "Full-time", employee_level: 1 };
  try {
    await prisma.$transaction(async (tx) => {
      for (const profile of profiles) {
        await tx.employees.upsert({
          where: { email: profile.email },
          update: {},
          create: {
            ...profile,
            ...employment,
            password_hash,
            must_change_password: false,
          },
        });
        await tx.employees.updateMany({
          where: { email: profile.email, password_hash: null },
          data: { password_hash, must_change_password: false },
        });
        await tx.employees.updateMany({
          where: {
            email: profile.email,
            employment_type: null,
            employee_level: null,
          },
          data: employment,
        });
      }
    });
    console.log(
      "Demo employee profiles ready. Existing data and passwords preserved.",
    );
  } catch {
    console.error(
      "Seed failed. Check the connection and apply migrations first.",
    );
    process.exitCode = 1;
  } finally {
    await prisma.$disconnect();
  }
}

void main();
