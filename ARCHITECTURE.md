# Architecture

Operating it in production (alerts, dashboards, recovery procedures) is in **[OPERATIONS.md](OPERATIONS.md)**.

## Overview

```
Browser ──► Next.js (Server Action) ──► POST /api/applications ──► SubmitApplicationHandler
                                                                     │ 1. DecisionEngine runs every deny rule
                                                                     │ 2. create or update Customer (+ its LoanApplication)
                                                                     │ 3. publish LoanApplicationApproved   ─┐ one SaveChanges
                                                                     │ 4. commit                             ┘ = one DB transaction
                                                                     ▼
                                              SQL Server: Customers, LoanApplications, OutboxMessages
                                                                     ▲   claim batch (UPDLOCK/READPAST, lease)
          any number of instances: OutboxProcessor ─► OutboxRelay ───┘
                                                         │ IntegrationEventDispatcher ─► IIntegrationEventHandler<T>
                                                         ▼
                                     LoanApplicationApprovedHandler ─► POST /customers (new) | PUT /customers/{id} (returning)
                                                                        Idempotency-Key + traceparent headers
```

## Project structure

Dependencies point inward: `Api → Infrastructure → Application → Domain`. Domain and Application have no reference to EF Core, ASP.NET, HTTP or OpenTelemetry — the compiler enforces it because each layer is its own project.

| Project / folder | Responsibility |
|---|---|
| `backend/src/LoanApp.Domain` | Business model and rules. `Customers/` — `Customer` aggregate root (owns its single `LoanApplication`), `Address`, `Applicant`. `Ssn` value object (normalizes, validates, masks itself in logs). `Decisions/` — the rule engine (`DecisionEngine`, `IDecisionRule`, `Rules/`). |
| `backend/src/LoanApp.Application` | Use cases and ports. `Applications/SubmitApplicationHandler` orchestrates decide → save → publish. `Events/` — `LoanApplicationApproved` and its handler. `Abstractions/` — ports (`ICustomerRepository`, `IUnitOfWork`, `IEventPublisher`, `IIntegrationEventHandler<T>`, `IExternalCustomerService`, `IEventDeliveryOperations`) and the exceptions that are part of their contract. `Diagnostics/` — trace source and business metrics (BCL `System.Diagnostics` only). |
| `backend/src/LoanApp.Infrastructure` | Adapters. `Persistence/` — `LoanDbContext` (also the `IUnitOfWork`), repository, migrations. `Outbox/` — publisher, relay, processor, dispatcher, retry policy, metrics, health check, operations. `ExternalService/` — typed `HttpClient` with circuit breaker. `Decisions/` — blacklist from configuration. |
| `backend/src/LoanApp.Api` | Composition root and HTTP: thin controllers, exception → Problem Details mapping (`ErrorHandling/`), OpenTelemetry setup, health endpoints, `--migrate-only` mode. |
| `web/` | Next.js. Form (`app/page.tsx`, `application-form.tsx`), Server Action that calls the API (`actions.ts`), `approved/` and `denied/` pages, `lib/` (validation/formatting). |
| `external-service-mock/` | Plain Node, no dependencies: in-memory store, honours `Idempotency-Key`, can simulate outages. |

## SOLID, concretely

| Principle | Where it shows |
|---|---|
| **S**ingle responsibility | The outbox is split by reason to change: `OutboxProcessor` (hosting/scheduling), `OutboxRelay` (claim + deliver), `IntegrationEventDispatcher` (route by type), `OutboxRetryPolicy` (when to retry / give up), `OutboxMetrics`, `OutboxHealthCheck`. The controller only binds and delegates. Each exception → HTTP mapping is its own handler. |
| **O**pen/closed | New deny rule = new `IDecisionRule` class, **no other file changes** (rules are discovered). New integration event = event type + `IIntegrationEventHandler<T>`; the dispatcher builds routes from what is registered (no `switch`). New error mapping = new `IExceptionHandler`. Verified by `DependencyInjectionTests` and `DecisionEngineTests.New_rules_plug_in_without_touching_existing_ones`. |
| **L**iskov | Every `IDecisionRule` honours the same contract (null = no objection), every `IExternalCustomerService` throws `PermanentFailureException` vs any other exception with the same meaning, so the relay treats them uniformly; tests swap fakes in without special cases. |
| **I**nterface segregation | Small role interfaces: the use case sees `ICustomerRepository` / `IEventPublisher` / `IUnitOfWork`, not a DbContext; the background handler sees only `IExternalCustomerService`; operators' needs (`IEventDeliveryOperations`) are separate from publishing. |
| **D**ependency inversion | Application owns the ports; Infrastructure implements them; Api wires them. Telemetry in inner layers uses BCL types only — the exporter is chosen in the composition root. |

Deliberately **not** abstracted: `DecisionEngine` is a pure domain service with no I/O and a single implementation — an `IDecisionEngine` would add a file and no flexibility (rules are already the extension point). Same for `OutboxRetryPolicy`: configurable through options, one implementation.

## Rule engine

```csharp
public interface IDecisionRule
{
    DenialReason? Evaluate(LoanRequest request);   // null = no objection
}
```

`DecisionEngine` receives every `IDecisionRule`, runs all of them (no short-circuit, so a denial lists every reason) and approves only if none denies. Rules: `RestrictedStateRule` (`STATE_NOT_SERVED`), `BlacklistedSsnRule` (`SSN_BLACKLISTED`, via the `ISsnBlacklist` port).

**Adding a rule:** add a class implementing `IDecisionRule` in `LoanApp.Domain/Decisions/Rules/`. That's it — `AddApplication()` discovers it. Optionally add an applicant-facing message for its code in `web/app/denied/page.tsx` (unknown codes get a generic message).

**Denials:** `200` with `status: "Denied"` and reason codes (a business outcome, not an error); the UI maps codes to friendly text and never discloses the blacklist. Denials are not stored, but are logged (SSN masked) and counted by reason (`loanapp.applications.denial_reasons`).

## Returning customer

Found by SSN (unique index). `Customer` is the aggregate root and owns exactly one `LoanApplication`, so "one customer, one application per SSN" is a model invariant: new SSN → `Customer.Create`, known SSN → `customer.Resubmit` (same ids, no new rows). The event carries `IsReturningCustomer` → create vs update downstream.

## Transaction and background event (transactional outbox)

Publishing to a broker (or `Task.Run`) cannot be atomic with the DB write (dual-write problem). So `IEventPublisher.Publish` inserts an `OutboxMessages` row in the same `DbContext`, and `IUnitOfWork.CommitAsync` saves everything in **one transaction**. The row also stores the event id (idempotency key), an ordering key (customer id) and the W3C `traceparent` of the request.

| Failure | Result |
|---|---|
| Rule engine denies / invalid input | Nothing written or published (200 Denied / 400). |
| DB fails saving customer, application **or** outbox row | Whole transaction rolled back, no event. 500 with a trace id the UI shows as a support reference. Test: `Failure_while_saving_rolls_back_customer_application_and_event` (a trigger makes the outbox insert fail inside SQL Server, for new and returning customers). |
| Transient DB fault (failover, network blip) | EF Core `EnableRetryOnFailure` retries the operation. |
| Two requests for the same SSN at the same time | Unique index (create race) or `rowversion` (update race) → `ConcurrencyConflictException` → **409**, nothing saved, UI asks to resubmit. Tests: `Concurrent_submissions_for_one_ssn_never_create_two_customers`, `Stale_update_of_a_customer_is_rejected_as_a_concurrency_conflict`. |
| External service down / timeout / 5xx / 429 | Data already committed. Retried with exponential backoff + jitter (2s → 60s cap), up to 10 attempts. Circuit breaker fails fast while the service is down. Test: `Failed_delivery_is_retried_later_instead_of_lost`. |
| External service rejects (other 4xx) | Not retryable → dead letter at once. Test: `Rejected_event_is_dead_lettered_reported_and_can_be_replayed`. |
| 10 failed attempts | Dead letter (`FailedAt`), logged as error, health → Degraded. Replay via operations API (OPERATIONS.md). |
| Worker crashes mid-delivery | Its lease expires (5 min) and another instance redelivers. Test: `Lease_of_a_crashed_worker_expires_and_the_event_is_redelivered`. |
| Graceful shutdown | In-flight deliveries are cancelled and their lease released immediately (no attempt counted). |

## Distributed delivery: guarantees and how they are met

- **At-least-once delivery, effectively-once effect.** A message is marked processed only after a 2xx. A crash between the 2xx and that update causes a redelivery, which the receiver de-duplicates by `Idempotency-Key` (the event id; the mock implements it). Payloads are full state, not diffs, so even a receiver without idempotency keys converges.
- **Competing consumers.** Any number of instances can run the relay (`docker compose` runs `api` and `worker`). Claiming is a single `UPDATE … OUTPUT` over a CTE with `UPDLOCK, READPAST`: two workers never claim the same row and never wait on each other. Test: `Parallel_workers_deliver_each_event_exactly_once_and_in_order_per_customer` (4 workers, 16 events, no duplicates).
- **Per-customer ordering without head-of-line blocking.** A message is claimable only if no earlier message with the same ordering key is unprocessed (pending, leased *or* dead-lettered). A customer's update never overtakes its create — even across instances and retries — while other customers keep flowing. Test: `A_failing_customer_blocks_only_its_own_later_events`.
- **Leases, not locks held during I/O.** The claim commits immediately; the HTTP call happens outside any DB transaction. A dead worker's lease expires.
- **Trace continuity.** Delivery runs as a `Consumer` span whose parent is the stored `traceparent`, so one trace shows the HTTP request, the commit, every delivery attempt (on whichever instance) and the outgoing HTTP call; the external service receives the same trace in its `traceparent` header.

## External service contract

| Operation | Request | Response |
|---|---|---|
| Create | `POST /customers` + `Idempotency-Key` | `200 { "id" }`; a repeated key returns the first response without re-applying |
| Update | `PUT /customers/{customerId}` + `Idempotency-Key` | `200 { "id" }`; `404` if never created |

Body (full state, same for both): `{ "customer": { "id", "firstName", "lastName", "ssn", "address": {…}, "companyName" }, "application": { "id", "customerId", "requestedAmount" } }`.

Our `Customer.Id` is the external id (nothing to map back). Error classification: 2xx = done; 408/429/5xx/network/timeout/open circuit = transient → retry; any other 4xx = permanent → dead letter. Retries are owned by the outbox (durable, survive restarts), not by the HTTP client (which only has a timeout and a circuit breaker, to avoid retry storms).

## Observability

- **Traces** (OpenTelemetry): ASP.NET Core, HttpClient, SqlClient and our own `LoanApp` source; propagated through the outbox as above. Idle outbox polling is sampled out so it doesn't bury real traces.
- **Metrics**: `loanapp.applications.decisions` {decision, customer.type}, `loanapp.applications.denial_reasons` {reason}, `loanapp.outbox.deliveries` {event.type, outcome = delivered | retry | dead_letter}, `loanapp.outbox.delivery_lag` (commit → delivered, seconds), plus ASP.NET Core / HttpClient / runtime metrics.
- **Logs**: structured, with scopes (trace id, outbox message id, event id, event type, ordering key); JSON in containers; also exported via OTLP so logs, traces and metrics correlate. SSNs are masked (`Ssn.ToString()`).
- **Health**: `/health/live` (process), `/health/ready` (database — use for load balancer/readiness), `/health` (everything, JSON; the outbox reports **Degraded** on dead letters or backlog older than 5 min — an alert, not a reason to stop traffic).
- **Export**: OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. `docker compose` includes the Aspire dashboard (http://localhost:18888); in production point it at any OTLP collector.

## Trade-offs (what I left out, and why)

- **No message broker.** Outbox table + relay gives atomicity, competing consumers, ordering and retries with zero extra infrastructure. To add a broker later, the relay publishes to it instead of calling the handler directly; the outbox stays (it is what makes publishing transactional).
- **Polling (1s)** instead of change notifications: simplest correct option; latency ≤ 1s plus backoff.
- **Sequential delivery inside a batch.** Parallelism comes from more instances; per-instance parallelism (each message in its own scope) is a small change if throughput requires it.
- **Leases are fixed (5 min)**, not renewed: must exceed batch size × HTTP timeout (10 × 10s). Renewal would allow shorter recovery after a crash.
- **Operations endpoints are unauthenticated** like the rest of the exercise; in production they go behind an admin policy / internal network.
- **SSN in plain text** in DB, outbox payload and outgoing call. Production: column encryption + deterministic hash for lookups, payload encryption or tokenization.
- **Denied applications not persisted** (not required) — logged and counted instead; first thing to add for audit.
- **Migrations**: a one-shot `--migrate-only` job in compose (the production pattern); `dotnet run` still migrates on startup for convenience (`Database:MigrateOnStartup`).
- **Next.js Server Action** calls the API server-side: no CORS, the browser never calls the API directly, SSNs never in URLs.
- Not added because nothing needs them: MediatR/CQRS, generic repository, AutoMapper, a broker, Kubernetes manifests, CI, auth.
