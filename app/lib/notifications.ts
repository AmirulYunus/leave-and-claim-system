import type { Prisma } from "../generated/prisma/client";
import type { Message } from "./notification-messages";

type Tx = Prisma.TransactionClient;

// Active HR accounts receive HR notifications.
export async function hrEmails(tx: Tx) {
  const hr = await tx.employees.findMany({
    where: { role: "HR", is_active: true },
    select: { email: true },
  });
  return hr.map((person) => person.email);
}

// Queue emails in the caller's transaction, so a decision and its
// notifications are saved together. Delivery happens separately.
export async function queueEmails(
  tx: Tx,
  emails: (Message & { to: string; key?: string })[],
) {
  if (!emails.length) return;
  await tx.notifications.createMany({
    data: emails.map((email) => ({
      recipient: email.to,
      subject: email.subject,
      body: email.body,
      dedupe_key: email.key ?? null,
    })),
    skipDuplicates: true,
  });
}

// The employee's message plus one HR message to each HR account.
export async function queueForEmployeeAndHr(
  tx: Tx,
  employeeEmail: string,
  messages: { employee: Message; hr: Message },
) {
  await queueEmails(tx, [
    { to: employeeEmail, ...messages.employee },
    ...(await hrEmails(tx)).map((to) => ({ to, ...messages.hr })),
  ]);
}
