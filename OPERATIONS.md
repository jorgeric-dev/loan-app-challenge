# Operations runbook

How to know the system is healthy, what to alert on, and how to recover. Endpoints are relative to the API (`http://localhost:5080` locally).

## Signals

| What | Where | Healthy | Alert when |
|---|---|---|---|
| Process up | `GET /health/live` | 200 | non-200 → restart the instance (liveness probe) |
| Can serve traffic | `GET /health/ready` (database) | 200 | 503 → take the instance out of rotation (readiness probe); DB issue |
| Event delivery | `GET /health` → `checks.outbox` | `Healthy` | `Degraded` for > 5 min: dead letters or backlog older than 5 min |
| Backlog | `GET /api/operations/events/status` | `pending` near 0 | `oldestPendingAge` growing |
| Delivery outcomes | metric `loanapp.outbox.deliveries` by `outcome` | mostly `delivered` | any `dead_letter`; `retry` rate spike |
| Delivery lag | metric `loanapp.outbox.delivery_lag` | p95 of a few seconds | p95 > 1 min |
| Business | `loanapp.applications.decisions`, `…denial_reasons` | stable ratios | approval rate drops to 0 / denial reason spikes (bad rule or bad data) |
| Errors | logs `LogLevel = Error`, HTTP 5xx | none | any; the 500 response and the UI show a trace id — search it in the traces/logs |

Locally all of this is in the Aspire dashboard (http://localhost:18888): traces, structured logs and metrics from both `loan-api` and `loan-worker`.

## Playbooks

### External service outage

*Symptoms:* `retry` outcomes rising, warnings `Delivery of … failed`, circuit breaker opening, `/health` Degraded once the backlog is older than 5 minutes.

1. Nothing to do for data safety: applications keep being approved and committed; events wait in `OutboxMessages`.
2. Messages are retried with backoff up to 10 attempts (~5 min). Longer outages dead-letter them (step 3).
3. When the service is back: `POST /api/operations/events/dead-letters/requeue` → all dead letters get a fresh retry budget and are delivered **in their original per-customer order** (later events of the same customer were held back behind them).

Try it locally: `curl -X POST "localhost:4000/admin/fail?count=5"`, submit an application, watch the mock logs and `/api/operations/events/status`.

### Dead letter caused by a rejected payload (4xx)

*Symptoms:* error log `Gave up delivering …` after 1 attempt, `GET /api/operations/events/dead-letters` shows `lastError` like `PUT /customers/… returned 422: …`.

1. Read `lastError`; find the full trace by the event id / ordering key.
2. Fix the cause (contract mismatch with the external team, bad data, missing customer on their side → for a `404` on update, re-create it there first).
3. Requeue that message: `POST /api/operations/events/dead-letters/{id}/requeue`. Later events of that customer are delivered right after it.

### A worker instance crashed or was killed

Nothing to do. Its claimed messages become available when the lease expires (`Outbox:LeaseDuration`, 5 min) and another instance delivers them. Graceful shutdowns release leases immediately. Duplicate deliveries that may result are absorbed by the `Idempotency-Key`.

### Database unavailable / failover

- Requests: transient faults are retried by EF Core; if the DB stays down, submissions return 500 (nothing half-saved) and `/health/ready` returns 503 so traffic is drained.
- Outbox workers log `Outbox polling failed` and retry every second; they resume by themselves.
- After a **restore from backup**, events committed after the backup point are lost together with the data they describe; events between the backup and the last delivery may be delivered again — idempotency keys make that harmless. Reconcile with the external service if the gap matters.

### "We submitted twice and got an error"

`409 Concurrent submission`: two submissions for the same SSN raced; one won, the other saved nothing. Resubmitting is safe (it becomes an update).

## Deployments

1. Run migrations once: `docker compose run --rm migrations` (in production, the same image with `--migrate-only` as a pipeline step / init job). Migrations are written to be backward compatible with the running version (add columns, backfill, then use).
2. Roll out instances. Any number can run; set `Outbox__Enabled=false` on instances that should only serve HTTP.
3. Rollback: stop new instances; pending outbox rows are processed by whichever version runs (payloads are versioned by event type name — add new event types rather than changing existing ones).

## Housekeeping

Processed outbox rows are purged hourly after `Outbox:Retention` (7 days). Dead letters are never purged automatically.

## Tuning (`appsettings.json` → `Outbox`, `ExternalCustomerService`)

`PollingInterval` (1s), `BatchSize` (10), `LeaseDuration` (5 min — keep > BatchSize × `ExternalCustomerService:Timeout`), `MaxAttempts` (10), `BaseRetryDelay` (2s), `MaxRetryDelay` (1 min), `Retention` (7 days), `MaxHealthyBacklogAge` (5 min), `ExternalCustomerService:Timeout` (10s).
