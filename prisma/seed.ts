import { loadEnvConfig } from "@next/env";
import { database } from "../app/lib/prisma";

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
  try {
    await prisma.$transaction(async (tx) => {
      for (const profile of profiles) {
        await tx.employees.upsert({
          where: { email: profile.email },
          update: {},
          create: profile,
        });
      }
    });
    console.log("Demo employee profiles ready. Existing data preserved.");
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
