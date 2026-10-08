"use client";

import { useState, useTransition, type ReactNode } from "react";
import { submitApplication } from "./actions";
import { emptyForm, formatSsn, validate, type ApplicationForm, type FieldErrors } from "@/lib/application-form";
import { US_STATES } from "@/lib/us-states";

export function ApplicationFormView() {
  const [form, setForm] = useState<ApplicationForm>(emptyForm);
  const [touched, setTouched] = useState<Partial<Record<keyof ApplicationForm, boolean>>>({});
  const [submitted, setSubmitted] = useState(false);
  const [serverErrors, setServerErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const clientErrors = validate(form);
  const errorFor = (field: keyof ApplicationForm) =>
    serverErrors[field] ?? (submitted || touched[field] ? clientErrors[field] : undefined);

  const update = (field: keyof ApplicationForm, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setServerErrors((current) => ({ ...current, [field]: undefined }));
  };

  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitted(true);
    setMessage(null);
    if (Object.keys(clientErrors).length > 0) {
      setMessage("Please fix the highlighted fields.");
      return;
    }
    startTransition(async () => {
      // On success the action redirects to /approved or /denied; it only returns on error.
      const error = await submitApplication(form);
      setMessage(error.message);
      setServerErrors(error.fieldErrors ?? {});
    });
  };

  const field = (name: keyof ApplicationForm, label: string, input: ReactNode, hint?: string, className = "") => (
    <div className={`field ${className}`}>
      <label htmlFor={name}>{label}</label>
      {input}
      {errorFor(name) ? (
        <div className="error" id={`${name}-error`} role="alert">
          {errorFor(name)}
        </div>
      ) : (
        hint && <div className="hint">{hint}</div>
      )}
    </div>
  );

  const textProps = (name: keyof ApplicationForm, autoComplete: string) => ({
    id: name,
    name,
    value: form[name],
    autoComplete,
    "aria-invalid": Boolean(errorFor(name)),
    "aria-describedby": errorFor(name) ? `${name}-error` : undefined,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => update(name, e.target.value),
    onBlur: () => setTouched((current) => ({ ...current, [name]: true })),
  });

  return (
    <form className="card" onSubmit={onSubmit} noValidate>
      {message && (
        <div className="alert" role="alert">
          {message}
        </div>
      )}

      <fieldset>
        <legend>About you</legend>
        <div className="grid">
          {field("firstName", "First name", <input {...textProps("firstName", "given-name")} />)}
          {field("lastName", "Last name", <input {...textProps("lastName", "family-name")} />)}
          {field(
            "ssn",
            "Social Security Number",
            <input
              {...textProps("ssn", "off")}
              inputMode="numeric"
              placeholder="123-45-6789"
              onChange={(e) => update("ssn", formatSsn(e.target.value))}
            />,
            "Used to identify you. If you applied before, we'll update your existing application.",
            "full",
          )}
        </div>
      </fieldset>

      <fieldset>
        <legend>Your business</legend>
        <div className="grid">
          {field("companyName", "Company name", <input {...textProps("companyName", "organization")} />)}
          {field(
            "requestedAmount",
            "Requested amount",
            <div className="prefix-input">
              <span>$</span>
              <input
                {...textProps("requestedAmount", "off")}
                inputMode="numeric"
                placeholder="25,000"
                value={form.requestedAmount ? Number(form.requestedAmount).toLocaleString("en-US") : ""}
                onChange={(e) => update("requestedAmount", e.target.value.replace(/\D/g, "").slice(0, 10))}
              />
            </div>,
          )}
        </div>
      </fieldset>

      <fieldset>
        <legend>Address</legend>
        <div className="grid">
          {field("street", "Street address", <input {...textProps("street", "street-address")} />, undefined, "full")}
          {field("city", "City", <input {...textProps("city", "address-level2")} />)}
          {field(
            "state",
            "State",
            <select {...textProps("state", "address-level1")}>
              <option value="">Select a state</option>
              {US_STATES.map((state) => (
                <option key={state.code} value={state.code}>
                  {state.name}
                </option>
              ))}
            </select>,
          )}
          {field("zipCode", "ZIP code", <input {...textProps("zipCode", "postal-code")} inputMode="numeric" maxLength={10} />)}
        </div>
      </fieldset>

      <div className="actions">
        <button type="submit" className="button" disabled={pending}>
          {pending ? "Submitting…" : "Submit application"}
        </button>
        <span className="muted">You&apos;ll get a decision instantly.</span>
      </div>
    </form>
  );
}
