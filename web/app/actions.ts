"use server";

import { redirect } from "next/navigation";
import type { ApplicationForm, SubmitError } from "@/lib/application-form";

// Server-side only: the browser talks to Next.js, Next.js talks to the API (no CORS, API not exposed).
const API_URL = process.env.API_URL ?? "http://localhost:5080";

type SubmitApplicationResult = {
  status: "Approved" | "Denied";
  applicationId: string | null;
  isReturningCustomer: boolean;
  denialReasons: string[];
};

type ProblemDetails = { detail?: string; traceId?: string; errors?: Record<string, string[]> };

export async function submitApplication(form: ApplicationForm): Promise<SubmitError> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}/api/applications`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ...form, requestedAmount: Number(form.requestedAmount) }),
      cache: "no-store",
    });
  } catch {
    return { message: "We couldn't reach our servers. Please try again in a moment." };
  }

  if (response.status === 400) {
    const problem = (await response.json()) as ProblemDetails;
    return { message: problem.detail ?? "Please review the highlighted fields.", fieldErrors: toFieldErrors(problem.errors) };
  }
  if (response.status === 409) {
    return { message: "This application was submitted at the same time from somewhere else and was not saved. Please submit again." };
  }
  if (!response.ok) {
    // The trace id lets support find this exact request in the logs/traces.
    const problem = (await response.json().catch(() => ({}))) as ProblemDetails;
    const reference = problem.traceId ? ` (reference: ${problem.traceId})` : "";
    return { message: `Something went wrong on our side and your application was not saved. Please try again${reference}.` };
  }

  const result = (await response.json()) as SubmitApplicationResult;
  if (result.status === "Approved") {
    const query = new URLSearchParams({ ref: result.applicationId ?? "" });
    if (result.isReturningCustomer) query.set("returning", "1");
    redirect(`/approved?${query}`);
  }
  redirect(`/denied?${new URLSearchParams(result.denialReasons.map((reason) => ["reason", reason]))}`);
}

// API validation keys are property names ("Ssn", "$.requestedAmount"); map them back to form fields.
function toFieldErrors(errors: ProblemDetails["errors"]): SubmitError["fieldErrors"] {
  if (!errors) return undefined;
  return Object.fromEntries(
    Object.entries(errors).map(([key, messages]) => {
      const name = key.replace(/^\$\./, "");
      return [name.charAt(0).toLowerCase() + name.slice(1), messages[0]];
    }),
  );
}
