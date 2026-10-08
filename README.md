# Loan Application

> 🎥 **Video walkthrough:** _TODO: add the public Loom link here._

A small loan application flow: a Next.js form → a .NET API that runs a rule engine → approved applications are saved in SQL Server → background workers send them to an external (mock) service over HTTP.

- **[ARCHITECTURE.md](ARCHITECTURE.md)** — layers, SOLID, rule engine, transactional outbox, distributed delivery guarantees, observability, trade-offs.
- **[OPERATIONS.md](OPERATIONS.md)** — production runbook: signals and alerts, outage / dead-letter / crash / failover playbooks, deployments.

| Part | Tech | Folder | Local URL |
|---|---|---|---|
| Frontend | Next.js 16 (App Router, Server Actions), plain CSS | `web/` | http://localhost:3000 |
| Backend | .NET 10, ASP.NET Core, EF Core 10, OpenTelemetry | `backend/` | http://localhost:5080 (OpenAPI: `/openapi/v1.json`, health: `/health`) |
| Database | SQL Server 2022 | Docker | `localhost,1433` (sa / `LoanApp!Passw0rd`) |
| External service | Mock in plain Node.js, no dependencies | `external-service-mock/` | http://localhost:4000/customers |
| Observability | Aspire dashboard (OTLP: traces, metrics, logs) | Docker | http://localhost:18888 |

## Run everything (Docker — recommended)

Requires Docker.

```bash
docker compose up --build
```

Open http://localhost:3000. Compose runs the production shape: a one-shot `migrations` job, then two backend instances (`api` serves HTTP, `worker` is a second instance competing for the same outbox) — see ARCHITECTURE.md › *Distributed delivery*.

- What the external service received: http://localhost:4000/customers, or live: `docker compose logs -f external-service`
- Traces / metrics / structured logs of both instances: http://localhost:18888
- Event delivery status and dead letters: http://localhost:5080/api/operations/events/status, `/api/operations/events/dead-letters`
- Simulate an external outage (next 5 calls fail): `curl -X POST "localhost:4000/admin/fail?count=5"` — then watch retries, ordering and recovery (OPERATIONS.md).

## Run each part locally (without Docker for the apps)

Requires .NET 10 SDK and Node.js 20+. SQL Server still comes from Docker (or point the connection string at your own server — see below).

```bash
# 1. Database
docker compose up -d sqlserver

# 2. External service mock (port 4000)
node external-service-mock/server.js

# 3. API (port 5080) — applies migrations on startup in this mode
dotnet run --project backend/src/LoanApp.Api

# 4. Frontend (port 3000)
cd web && npm install && npm run dev
```

**Using your own SQL Server** (e.g. LocalDB on Windows) instead of Docker — set the connection string before step 3 and before running the tests:

```powershell
$env:ConnectionStrings__LoanDb = "Server=(localdb)\MSSQLLocalDB;Database=LoanApp;Trusted_Connection=True;TrustServerCertificate=True"
```

To export telemetry in this mode, set `OTEL_EXPORTER_OTLP_ENDPOINT` (e.g. `http://localhost:4317` with any OTLP collector).

## Run the tests

```bash
docker compose up -d sqlserver   # integration tests need a real SQL Server
dotnet test backend
```

- `LoanApp.UnitTests` (36, no I/O): rule engine and rule discovery, `Customer` aggregate, `Ssn`, the submit use case (new, returning, denied), retry policy (backoff, jitter, give-up, permanent errors), HTTP client (POST/PUT, idempotency key, transient vs permanent status codes).
- `LoanApp.IntegrationTests` (16, real API + real SQL Server, throw-away database per test class):
  - endpoint: approved / denied (state, blacklist) / returning / invalid input;
  - **rollback** when the transaction fails (new and returning customer);
  - **concurrency**: simultaneous submissions for one SSN (no duplicates, clean 409), stale update rejected;
  - **distributed delivery**: 4 parallel workers deliver each event exactly once and in per-customer order; a failing customer doesn't block others; retry after transient failure; permanent failure → dead letter → reported in `/health` → replayed via the operations API; expired lease of a crashed worker is reclaimed;
  - trace context stored with the event.

Frontend: `cd web && npm run lint && npm run build`.

## Test data

Any SSN format works (`123-45-6789`, `123456789`). All fields are required.

| To get… | Type |
|---|---|
| **Approved** (new customer) | Any state except New York, any SSN not in the blacklist, e.g. `222-33-4444` |
| **Denied — state** | State = **New York** |
| **Denied — blacklisted SSN** | SSN `111-11-1111`, `123-45-6789` or `666-66-6666` |
| **Returning customer** | Submit again with an SSN that was already approved (e.g. `222-33-4444`) and change the name, address or amount. The success page says *"Your application was updated"*; the DB still has one customer and one application, and the mock logs `customer updated` (PUT) instead of `customer created` (POST). |

The blacklist lives in `backend/src/LoanApp.Api/appsettings.json` (`SsnBlacklist`).

Inspect the database:

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "LoanApp!Passw0rd" -C \
  -Q "SELECT * FROM LoanApp.dbo.Customers; SELECT * FROM LoanApp.dbo.LoanApplications; SELECT Id, Type, OrderingKey, Attempts, LockedBy, ProcessedAt, FailedAt, LastError FROM LoanApp.dbo.OutboxMessages"
```

## API

`POST /api/applications`

```json
{ "firstName": "Ann", "lastName": "Lee", "ssn": "222-33-4444", "companyName": "Acme",
  "requestedAmount": 25000, "street": "1 Main St", "city": "Austin", "state": "TX", "zipCode": "78701" }
```

- `200` — `{ "status": "Approved", "customerId", "applicationId", "isReturningCustomer": false, "denialReasons": [] }`
- `200` — `{ "status": "Denied", "denialReasons": ["STATE_NOT_SERVED"] }` (a denial is a business outcome, not an error)
- `400` — Problem Details for invalid input (missing field, SSN without 9 digits, amount ≤ 0)
- `409` — a concurrent submission for the same SSN won the race; nothing was saved, resubmit
- `500` — Problem Details with a `traceId` (shown in the UI as a support reference); nothing was saved

Operations (see OPERATIONS.md): `GET /api/operations/events/status`, `GET /api/operations/events/dead-letters`, `POST /api/operations/events/dead-letters/{id}/requeue`, `POST /api/operations/events/dead-letters/requeue`. Health: `/health/live`, `/health/ready`, `/health`.

## What's missing / not done

- **Video link** — to be added at the top of this file.
- No authentication (not required) — including the operations endpoints, which in production belong behind an admin policy.
- SSNs are stored and sent in plain text — see *Trade-offs* in ARCHITECTURE.md.
- No frontend unit tests: the form logic is small; the UI flows were verified end-to-end in a browser.
