import Link from "next/link";

export default async function ApprovedPage({ searchParams }: PageProps<"/approved">) {
  const { ref, returning } = await searchParams;
  const isReturning = returning === "1";

  return (
    <div className="card">
      <div className="result-icon success" aria-hidden>
        ✓
      </div>
      <h1>{isReturning ? "Your application was updated" : "Your application is approved"}</h1>
      <p className="lead">
        {isReturning
          ? "Welcome back! We found your existing application and updated it with the details you just sent."
          : "Great news — you're approved. We'll be in touch shortly with the next steps."}
      </p>
      {typeof ref === "string" && ref && (
        <p className="muted">
          Application reference: <code>{ref}</code>
        </p>
      )}
      <Link href="/" className="button">
        Back to the form
      </Link>
    </div>
  );
}
