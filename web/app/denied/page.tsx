import Link from "next/link";

// Applicant-facing wording for each rule-engine reason code. Blacklist matches are deliberately not disclosed.
const REASON_MESSAGES: Record<string, string> = {
  STATE_NOT_SERVED: "We don't offer loans to businesses located in New York at this time.",
  SSN_BLACKLISTED: "We weren't able to verify your eligibility with the information provided.",
};
const FALLBACK_MESSAGE = "Your application doesn't meet our current lending criteria.";

export default async function DeniedPage({ searchParams }: PageProps<"/denied">) {
  const { reason } = await searchParams;
  const codes = [reason ?? []].flat();
  const messages = [...new Set(codes.map((code) => REASON_MESSAGES[code] ?? FALLBACK_MESSAGE))];

  return (
    <div className="card">
      <div className="result-icon denied" aria-hidden>
        ✕
      </div>
      <h1>We can&apos;t approve your application</h1>
      <p className="lead">Thank you for applying. Unfortunately we&apos;re unable to offer you a loan right now.</p>
      <ul className="reasons">
        {(messages.length ? messages : [FALLBACK_MESSAGE]).map((message) => (
          <li key={message}>{message}</li>
        ))}
      </ul>
      <p className="muted">No information from this application was stored.</p>
      <Link href="/" className="button">
        Back to the form
      </Link>
    </div>
  );
}
