# UP Bazaar API

Modular monolith for the UP Bazaar marketplace: ASP.NET Core Web API, EF Core code-first
against SQL Server, Hangfire for background work, Serilog for logging.

## Running it

```bash
dotnet run --project src/UPBazaar.Api
```

Swagger is at `/swagger`, Scalar at `/scalar`, health at `/health`. Both documentation
endpoints are Development-only.

The development connection string in `appsettings.Development.json` points at
`.\SQLEXPRESS`. Change it, or override it without touching the file:

```bash
dotnet user-secrets set "ConnectionStrings:UPBazaar" "<your connection string>" --project src/UPBazaar.Api
```

Create or update the database:

```bash
dotnet ef database update -p src/UPBazaar.Infrastructure -s src/UPBazaar.Api
```

`openapi.json` at the repository root is the contract the Angular workspace in `web/` generates
its client from. It is a build artifact, not something to hand-edit — refresh it by running the
API and fetching the document:

```bash
curl -s http://localhost:5199/openapi/v1.json -o openapi.json
```

The document declares a relative server URL, so the file is identical on every machine.

## Testing

```bash
dotnet test
```

Unit tests need nothing. Integration tests need SQL Server and pick it up in this order:

1. `UPBAZAAR_TEST_SQL` — a server-level connection string; each run creates and drops its own
   database on that server.
2. A SQL Server container, when a Docker daemon is reachable.
3. Neither — the database tests report as **skipped**, not failed.

```bash
# example: reuse a local SQL Server Express instead of Docker
UPBAZAAR_TEST_SQL="Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True" dotnet test
```

Integration tests authenticate through a test scheme that reads permissions from an
`X-Test-Permissions` header, so the real policy provider is exercised and a missing
permission still produces a 403.

The Angular front ends live in [`web/`](web/README.md) and generate their API client from that
same `openapi.json`.

## Layout

```
src/
  UPBazaar.SharedKernel          primitives, Result, messaging contracts - no dependencies
  UPBazaar.Infrastructure        DbContext, outbox, audit, idempotency, auth, external I/O
  UPBazaar.Api                   host: DI wiring, auth, OpenAPI, Hangfire, migrations target
  UPBazaar.Modules.<Name>            domain + application + controllers for one module
  UPBazaar.Modules.<Name>.Contracts  the only thing other modules may reference
tests/
  UPBazaar.UnitTests             domain logic, no I/O
  UPBazaar.IntegrationTests      WebApplicationFactory + real SQL Server
```

Each module owns a SQL schema — `catalog`, `ordering`, `payments`, `shipping` — plus the
shared `shared` and `audit` schemas. A module never queries another module's tables: it either
calls an interface from that module's `.Contracts` project, or reacts to a domain event.

## How the pieces fit

**Messaging.** Controllers depend only on `IDispatcher`. It resolves the single handler for a
command or query and runs any FluentValidation validators first, so handlers assume a
well-formed request and controllers stay thin. Handlers return `Result`, which
`ResultExtensions` maps to an HTTP status and RFC 7807 problem details.

**Outbox.** Domain events raised on an aggregate are written to `shared.OutboxMessage` inside
the same transaction as the state change, by an EF interceptor. A Hangfire recurring job drains
the outbox and dispatches each event to `IDomainEventHandler<T>` implementations, wherever they
live. That is the only sanctioned way an action in one module causes an effect in another —
for example, placing an order books a shipment.

**Idempotency.** Checkout, refunds and the Razorpay webhook all reserve a key in
`shared.IdempotencyRecord` before doing any work. A retry with the same key replays the stored
response; the same key with a different body is a 409. The unique index on the key is what makes
a concurrent duplicate lose the race instead of running twice.

**Auditing.** Implementing `IAuditable` is the whole opt-in. The persistence interceptor stamps
the entity and writes an `audit.AuditLog` row with the changed properties, redacting anything
whose name looks sensitive.

**Authorization.** Every endpoint carries `[Authorize("<permission>")]` unless explicitly
`[AllowAnonymous]`, and a fallback policy rejects anything that forgets. `PermissionPolicyProvider`
turns a permission name into a policy on demand, so adding an endpoint never means editing a
central list. Permission names live in each module's Contracts project.

**External I/O.** Razorpay, Shiprocket, SMS, email and blob storage sit behind interfaces in
`UPBazaar.Infrastructure/ExternalServices`, each with a `Fake*` implementation.
`ExternalServices:UseSandbox` selects between them and defaults to `true`.

## Conventions worth knowing

- Every schema change ships with a migration in the same commit. Applied migrations are never edited.
- Money is `decimal(18,2)`, timestamps are `datetime2` UTC, ids are `bigint` identity plus a
  `Guid PublicId` that is the only id crossing a module or API boundary. The first two are
  enforced globally in `UPBazaarDbContext.ConfigureConventions`.
- `Stock`, `Payment` and `SettlementLine` carry `rowversion` concurrency tokens.
- Nothing that looks like PII or a secret is logged: `SensitiveDataDestructuringPolicy` masks it
  on the way to the sink, and the audit interceptor masks it on the way to the database.
- No secrets in `appsettings.*.json`. Use user-secrets or environment variables.
- Each module has a `.http` file next to it holding a request for every endpoint it exposes.

## Current state

Everything above is wired and covered by tests. Deliberately not built yet:

- **Live payment and shipping adapters.** Only the sandbox fakes exist. Starting the app with
  `ExternalServices:UseSandbox=false` fails fast with a message naming what to implement.
- **Identity.** JWT bearer validation is configured against a symmetric key, but nothing issues
  tokens. Development falls back to an ephemeral signing key so a fresh clone still runs.
- **Categories** have no endpoints yet; the catalog exposes products and stock only.
- **Seller payouts.** Settlement lines are created and reversed, but nothing pays them out.

## Target framework

The solution is on `net9.0` because that is the newest SDK installed on this machine. Moving to
.NET 10 is a one-line change to `TargetFramework` in `Directory.Build.props` plus the `EfVersion`
and `AspNetVersion` properties in `Directory.Packages.props`.
