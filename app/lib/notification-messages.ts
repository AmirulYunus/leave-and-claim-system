// Plain-text email content, adapted from the Dataverse notification flows.
// Messages name the request and decision only; reasons, medical details and
// documents are never included.
export type Message = { subject: string; body: string };

const appLink = () =>
  `View it in the leave and claims workspace: ${process.env.APP_URL ?? "http://localhost:3000"}`;
const decided = (status: string) =>
  status === "Approved" ? "approved" : "rejected";
const describe = (request: {
  kind: string;
  category: string;
  detail: string;
}) =>
  `${request.kind === "Leave" ? request.category : `${request.category} claim`} (${request.detail})`;

export function requestDecisionMessages(input: {
  kind: string;
  category: string;
  detail: string;
  status: string;
  employeeName: string;
  reviewerName: string;
}): { employee: Message; hr: Message } {
  const what = describe(input);
  const result = decided(input.status);
  const noun = input.kind === "Leave" ? "leave request" : "claim";
  return {
    employee: {
      subject: `Your ${noun} was ${result}`,
      body: `Your ${what} was ${result} by ${input.reviewerName}.\n\n${appLink()}`,
    },
    hr: {
      subject: `${input.kind === "Leave" ? "Leave request" : "Claim"} ${result}`,
      body: `${input.employeeName}'s ${what} was ${result} by ${input.reviewerName}.\n\n${appLink()}`,
    },
  };
}

export function cancellationDecisionMessages(input: {
  category: string;
  detail: string;
  status: string;
  correction: boolean;
  employeeName: string;
  reviewerName: string;
}): { employee: Message; hr: Message } {
  const what = `${input.category} (${input.detail})`;
  const result = decided(input.status);
  const outcome =
    input.status === "Approved"
      ? "The leave is cancelled and its hours are returned to your balance."
      : "The leave remains approved.";
  const action = input.correction ? "HR correction" : "cancellation";
  return {
    employee: {
      subject: `Your leave ${action} was ${result}`,
      body: `The ${action} of your ${what} was ${result} by ${input.reviewerName}. ${outcome}\n\n${appLink()}`,
    },
    hr: {
      subject: `Leave ${action} ${result}`,
      body: `The ${action} of ${input.employeeName}'s ${what} was ${result} by ${input.reviewerName}.\n\n${appLink()}`,
    },
  };
}

export function carryForwardMessages(input: {
  employeeName: string;
  hours: number;
  sourceYear: number;
}): { employee: Message; hr: Message } {
  const days = input.hours / 8;
  const useBy = `31 March ${input.sourceYear + 1}`;
  return {
    employee: {
      subject: "Your Annual Leave carry-forward is confirmed",
      body: `HR confirmed ${days} days (${input.hours} hours) of Annual Leave carried forward from ${input.sourceYear}. Use it by ${useBy}; it is used before your ${input.sourceYear + 1} Annual Leave.\n\n${appLink()}`,
    },
    hr: {
      subject: "Annual Leave carry-forward confirmed",
      body: `Carry-forward of ${days} days (${input.hours} hours) from ${input.sourceYear} was confirmed for ${input.employeeName}, to use by ${useBy}.\n\n${appLink()}`,
    },
  };
}

// Sent to every active employee on 1 October and 30 November.
export function carryForwardReminder(year: number): Message {
  return {
    subject: "Annual Leave carry-forward reminder",
    body: `Please review your planned Annual Leave before 15 December ${year}. The standard carry-forward limit is five days. For a higher limit, obtain written approval and submit it to HR by 15 December. HR calculates the final unused amount after 31 December and confirms any carry-forward, which must be used by 31 March ${year + 1}.\n\n${appLink()}`,
  };
}

export const reminderDates = ["10-01", "11-30"];
