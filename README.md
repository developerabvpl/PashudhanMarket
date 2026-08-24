# UP Bazaar API

Modular monolith for the UP Bazaar marketplace. .NET 10, ASP.NET Core, EF Core 10 code-first
against SQL Server, Hangfire for background work, Serilog for logging.

Sixteen modules — Identity, Sellers, Catalog, Inventory, Cart, Orders, Payments, Shipping,
Settlements, Promotions, Reviews, Crm, Academy, Cms, Notifications, Reporting — each owning a
SQL schema and a `.Contracts` project that is the only thing other modules may reference.

## Running it

```bash
dotnet run --project src/UPBazaar.Api
```

| Surface | Path | Notes |
| --- | --- | --- |
| Swagger UI | `/swagger` | Development only |
| Scalar | `/scalar/v1` | Development only |
| OpenAPI document | `/openapi/v1.json` | one document per API version |
| Health | `/health` | self plus a SQL Server probe |
| Job dashboard | `/jobs` | requires the `platform.jobs.view` permission |
| API | `/api/v1/...` | version is a URL segment |

The development connection string in `appsettings.Development.json` points at `.\SQLEXPRESS`.
Override it without editing the file:

```bash
dotnet user-secrets set "ConnectionStrings:UPBazaar" "<connection string>" --project src/UPBazaar.Api
```

Every setting also accepts a `UPBAZAAR_`-prefixed environment variable, so
`UPBAZAAR_ConnectionStrings__UPBazaar` works in a container without colliding with anything
else on the host. The design-time migration factory reads the same variable.

Create or update the database:

```bash
dotnet ef database update -p src/UPBazaar.Infrastructure -s src/UPBazaar.Api
```

## Testing

```bash
dotnet test
```

Unit tests need nothing. Integration tests need SQL Server and resolve it in this order:

1. `UPBAZAAR_TEST_SQL` — a server-level connection string; each run creates and drops its own
   database on that server.
2. A SQL Server container via Testcontainers, when a Docker daemon is reachable.
3. Neither — the database tests report as **skipped**, not failed.

```bash
# reuse a local SQL Server Express instead of Docker
UPBAZAAR_TEST_SQL="Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True" dotnet test
```

The integration host turns on container scope validation, so a singleton that captures a
scoped service fails in CI rather than on someone's first `dotnet run`.

## Layout

```
src/
  UPBazaar.SharedKernel                primitives, Result, messaging contracts - no dependencies
  UPBazaar.Infrastructure              DbContext, interceptors, dispatcher, outbox processor
  UPBazaar.Api                         host: logging, auth, versioning, docs, health, jobs
  UPBazaar.Modules.<Name>              domain, application and endpoints for one module
  UPBazaar.Modules.<Name>.Contracts    interfaces, DTOs, permissions, published events
tests/
  UPBazaar.UnitTests                   domain and infrastructure logic, no I/O
  UPBazaar.IntegrationTests            WebApplicationFactory against a real SQL Server
```

A module never queries another module's tables. It either calls an interface from that
module's `.Contracts` project, or reacts to a domain event delivered through the outbox.

## How the pieces fit

**Dispatcher.** `IDispatcher` in SharedKernel, implemented in Infrastructure — roughly 150
lines instead of a MediatR dependency. It resolves the single handler for a command or query,
runs any registered FluentValidation validators first, and returns a `Result`. Handlers are
discovered by convention: writing `ICommandHandler<T>` in a module is the whole registration.

**One DbContext, schema per module.** `UPBazaarDbContext` applies
`IEntityTypeConfiguration` classes from every registered module's assembly, then forces each
module's entities into that module's schema even if a configuration forgot to say so. One
context means one migration history and one transaction per request, which is what lets the
outbox be atomic with the state change that raised the event.

**Interceptors.** `AuditInterceptor` stamps `IAuditable` entities and writes `shared.AuditLog`
rows describing what changed, with sensitive fields masked. `OutboxInterceptor` drains domain
events off aggregates into `shared.OutboxMessages`. Both run inside the caller's transaction,
so neither can record something a rollback undid.

**Outbox.** `OutboxProcessor` runs on a Hangfire recurring job, reads the oldest unprocessed
batch, and dispatches each event to its handlers wherever they live. Failures are recorded per
message with an attempt count, so one poison event cannot block the queue.

**Correlation.** Middleware honours an inbound `X-Correlation-Id` or mints one, sanitises it,
echoes it, and pushes it onto the Serilog context. Audit rows and outbox messages carry it too,
so one identifier ties a user action to every log line and side effect it produced.

**Errors.** A global `IExceptionHandler` returns RFC 7807 problem details carrying the
correlation id and nothing else — stack traces leak schema and file paths. Framework responses
(404, 405, 415) get the same shape through `AddProblemDetails`.

**Authorization.** A fallback policy denies by default, so an endpoint that forgets to declare
a policy is unreachable rather than accidentally public. Because that policy also applies to
requests matching no endpoint, an explicit anonymous fallback endpoint returns an honest 404
for unknown routes instead of a misleading 401.

## Conventions

- Warnings are errors, solution-wide. Three analyzer rules are switched off in `.editorconfig`,
  each with the reason written next to it.
- Money is `decimal(18,2)`, timestamps are `datetime2` UTC, ids are `bigint` identity plus a
  `Guid PublicId` — the only id that crosses a module or API boundary. The first two are
  enforced globally in `UPBazaarDbContext.ConfigureConventions`.
- `PublicId` is a version 7 GUID, so it sorts by creation time instead of fragmenting indexes.
- Every schema change ships with a migration in the same commit. Applied migrations are never
  edited.
- No secrets in `appsettings.*.json`.

## Current state

The platform is wired and tested; the modules are skeletons. Each has its registration, its
schema declaration and its permission names, and no entities yet — which is why `Baseline`
creates only the four `shared.*` tables.

Deliberately not built yet:

- **No token issuer.** JWT bearer validation is configured against a symmetric key, and
  Development falls back to an ephemeral one so a fresh clone runs. Issuance belongs to the
  Identity module.
- **Outbox processing is single-writer.** `DisableConcurrentExecution` keeps one worker on the
  queue. Running more than one instance needs a row-level claim added to `OutboxProcessor`.
- **`web/` targets the previous API.** The Angular workspace in `web/` and the `openapi.json`
  at the repository root describe the archived .NET 9 design, whose endpoints this rebuild does
  not yet have. Regenerate its client once the modules grow endpoints; until then the two are
  intentionally out of step.
