import { PrismaPg } from "@prisma/adapter-pg";
import { PrismaClient } from "../generated/prisma/client";

const globalPrisma = globalThis as unknown as { prisma?: PrismaClient };

export function database() {
  if (!process.env.DATABASE_URL)
    throw new Error("DATABASE_URL is not configured");
  return (globalPrisma.prisma ??= new PrismaClient({
    adapter: new PrismaPg({
      connectionString: process.env.DATABASE_URL,
      max: 5,
      connectionTimeoutMillis: 5000,
    }),
  }));
}
