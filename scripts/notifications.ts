// Email outbox jobs. Run on a schedule, for example every few minutes for
// `deliver` and daily for `remind`:
//   npm run notifications:deliver
//   npm run notifications:remind [-- --date=YYYY-MM-DD]
import { loadEnvConfig } from "@next/env";
import { database } from "../app/lib/prisma";
import { sendEmail } from "../app/lib/email-delivery";
import { malaysiaDate } from "../app/lib/entitlement";
import {
  carryForwardReminder,
  reminderDates,
} from "../app/lib/notification-messages";
import { queueEmails } from "../app/lib/notifications";

loadEnvConfig(process.cwd());
const maxAttempts = 5;

async function deliver() {
  const pending = await database().notifications.findMany({
    where: { sent_at: null, attempts: { lt: maxAttempts } },
    orderBy: { id: "asc" },
    take: 100,
  });
  let sent = 0;
  for (const email of pending) {
    try {
      await sendEmail(email);
      await database().notifications.update({
        where: { id: email.id },
        data: {
          sent_at: new Date(),
          attempts: { increment: 1 },
          last_error: null,
        },
      });
      sent++;
    } catch (error) {
      await database().notifications.update({
        where: { id: email.id },
        data: {
          attempts: { increment: 1 },
          last_error: error instanceof Error ? error.message : String(error),
        },
      });
    }
  }
  console.log(`Delivered ${sent} of ${pending.length} queued emails.`);
}

// Carry-forward reminders on 1 October and 30 November (Malaysia time). Each
// employee gets one per date even if the job runs more than once.
async function remind(date: string) {
  if (!reminderDates.includes(date.slice(5))) {
    console.log(`No carry-forward reminder is due on ${date}.`);
    return;
  }
  const employees = await database().employees.findMany({
    where: { is_active: true },
    select: { id: true, email: true },
  });
  const message = carryForwardReminder(Number(date.slice(0, 4)));
  await database().$transaction((tx) =>
    queueEmails(
      tx,
      employees.map((employee) => ({
        to: employee.email,
        ...message,
        key: `carry-forward-reminder:${date}:${employee.id}`,
      })),
    ),
  );
  console.log(
    `Queued carry-forward reminders for ${employees.length} employees.`,
  );
}

const [command, ...options] = process.argv.slice(2);
const date =
  options.find((option) => option.startsWith("--date="))?.slice(7) ??
  malaysiaDate();
const job =
  command === "deliver"
    ? deliver()
    : command === "remind"
      ? remind(date)
      : Promise.reject(new Error("Use: notifications.ts deliver | remind"));
job
  .catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  })
  .finally(() => database().$disconnect());
