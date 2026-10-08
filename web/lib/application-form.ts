export type ApplicationForm = {
  firstName: string;
  lastName: string;
  ssn: string;
  companyName: string;
  requestedAmount: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
};

export type FieldErrors = Partial<Record<keyof ApplicationForm, string>>;

export type SubmitError = { message: string; fieldErrors?: FieldErrors };

export const emptyForm: ApplicationForm = {
  firstName: "",
  lastName: "",
  ssn: "",
  companyName: "",
  requestedAmount: "",
  street: "",
  city: "",
  state: "",
  zipCode: "",
};

/** Client-side checks for fast feedback. The API validates again; this is only UX. */
export function validate(form: ApplicationForm): FieldErrors {
  const errors: FieldErrors = {};
  for (const [field, value] of Object.entries(form) as [keyof ApplicationForm, string][]) {
    if (!value.trim()) errors[field] = "Required";
  }
  if (form.ssn && form.ssn.replace(/\D/g, "").length !== 9) errors.ssn = "Enter all 9 digits";
  if (form.zipCode && !/^\d{5}(-\d{4})?$/.test(form.zipCode)) errors.zipCode = "Use 5 digits (or ZIP+4)";
  if (form.requestedAmount && Number(form.requestedAmount) <= 0) errors.requestedAmount = "Must be greater than $0";
  return errors;
}

/** 123456789 -> 123-45-6789 (partial input is formatted as the user types). */
export function formatSsn(value: string): string {
  const digits = value.replace(/\D/g, "").slice(0, 9);
  if (digits.length <= 3) return digits;
  if (digits.length <= 5) return `${digits.slice(0, 3)}-${digits.slice(3)}`;
  return `${digits.slice(0, 3)}-${digits.slice(3, 5)}-${digits.slice(5)}`;
}
