// Sends one queued email. EMAIL_DRIVER selects the provider; "log" (the
// default) prints the email and is for development only. Add the chosen
// provider (for example Amazon SES or SMTP) here.
export async function sendEmail(email: {
  recipient: string;
  subject: string;
  body: string;
}) {
  const driver = process.env.EMAIL_DRIVER ?? "log";
  if (driver === "log") {
    console.log(
      `--- Email to ${email.recipient}\nSubject: ${email.subject}\n\n${email.body}\n`,
    );
    return;
  }
  throw new Error(`Email driver "${driver}" is not implemented yet.`);
}
