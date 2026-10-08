import { ApplicationFormView } from "./application-form";

export default function ApplyPage() {
  return (
    <>
      <h1>Apply for a business loan</h1>
      <p className="lead">It takes about two minutes. All fields are required.</p>
      <ApplicationFormView />
    </>
  );
}
