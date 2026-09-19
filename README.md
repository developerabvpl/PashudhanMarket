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
| Auth | `/api/v1/auth/...` | register, login, OTP, refresh, logout, password reset, 2FA |

The development connection string in `appsettings.Development.json` points at the default local SQL Server instance (`Server=.`).
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
UPBAZAAR_TEST_SQL="Server=.;Trusted_Connection=True;TrustServerCertificate=True" dotnet test
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

## Identity

The first module with behaviour. It owns the `identity` schema: Users, Roles, Permissions,
RolePermissions, UserRoles, RefreshTokens, OtpChallenges and LoginAudit.

**Sign-in methods.** Email and password for sellers and staff, hashed with ASP.NET Identity's
PBKDF2 hasher and re-hashed transparently when the work factor rises. Mobile OTP for buyers:
requesting a code is rate limited per number and per IP, and verifying one creates the account
if it does not exist yet, because possession of the number has just been proved. Staff may
additionally enrol a TOTP authenticator.

**Tokens.** A 15-minute JWT carrying the user's permissions as claims, so authorising a request
costs no database round trip, plus a 30-day refresh token. Refresh tokens are stored only as
SHA-256 hashes, rotate on every use, and belong to a family. Presenting a token that was
already rotated is treated as theft: the whole family is revoked, both the attacker and the
victim are signed out, and the event is recorded.

**Authorization.** `PermissionCatalog` is the single source of truth. Policies are generated
from it at startup, the `identity.Permissions` table is seeded from it, and nine roles get their
permission sets there: SuperAdmin, Admin, CatalogModerator, FinanceOfficer, SupportAgent,
SupportSupervisor, AcademyAuthor, SellerOwner and Buyer. SuperAdmin is granted everything in the
catalogue by construction, so a permission added later is covered without editing a list.

**Enumeration resistance.** Every password failure returns one status and one message, whether
the account is missing, the password is wrong, or the account is locked. Forgot-password always
reports success. Requesting an OTP answers the same way for a registered and an unregistered
number.

**Seeding.** Permissions, roles and the first administrator are reconciled on every boot and
the operation is idempotent. Set the administrator through the environment:

```bash
UPBAZAAR_Identity__SuperAdmin__Email=admin@example.com
UPBAZAAR_Identity__SuperAdmin__Password=<a long passphrase>
```

Leaving them unset skips that step. The seeder logs a warning naming the account it created.

**Delivery.** SMS and email go through `ISmsSender` and `IEmailSender` in
`Notifications.Contracts`. Development gets senders that log the message — including the
one-time code, which is the point of them and the reason they refuse to register outside
Development. Every other environment gets senders that throw, so a host without a real provider
fails at the first send rather than silently dropping a login code.

## Current state

The platform is wired and tested. Identity, Catalog, Inventory and Cart are implemented; the
other twelve modules are skeletons with a registration, a schema declaration and their
permission names.

Catalog serves the published catalogue anonymously under `/api/v1/catalog` and takes changes
under `/api/v1/admin/catalog`. Products start as drafts, are published, and are archived rather
than deleted. In Development, `Catalog:SeedFile` imports `web/tools/data/catalog.json` into an
empty catalogue with its ids intact, so the storefront's product URLs survive the switch from
the bundled file to the API.

Inventory owns stock. Staff record deliveries, stock-takes and write-offs under
`/api/v1/admin/inventory`, and every change lands in a per-product ledger saying what, who and
why. Writing stock off needs `inventory.adjustments.approve`, which only Admin holds. Other
modules go through `IInventoryService`: Catalog reads stock levels to fill in its product
responses, and Cart and Orders will reserve stock through it. A reservation is all-or-nothing,
expires on its own (a Hangfire job sweeps every minute), and is then committed or released.
Concurrent reservations for the last unit are settled by a row version on the stock row, so
stock cannot be oversold.

Cart keeps one cart per signed-in buyer under `/api/v1/cart`; the Buyer role holds
`cart.read` and `cart.write`, and no route takes a cart id, so a buyer can only ever reach their
own. A cart stores products, quantities and the price each had when chosen - nothing else.
Names, current prices and stock are read from Catalog and Inventory on every request, and each
line is flagged `Unavailable`, `InsufficientStock` or `PriceChanged` when it cannot be bought as
shown. Guests keep the storefront's in-browser basket, which `POST /api/v1/cart/merge` folds in
at sign-in. Adding to a cart reserves nothing: stock is held at checkout, through
`ICartService` and Inventory's reservations, once Orders exists.

Deliberately not built yet:

- **Tokens are signed with a symmetric key.** Fine for one API; a second service verifying
  these tokens would want asymmetric signing and a JWKS endpoint.
- **No lockout on OTP sign-in.** The five-attempt cap per challenge bounds guessing, but there
  is no per-number lockout across challenges the way there is for passwords.
- **TOTP secrets are stored in plain text.** They should be encrypted at rest with a key the
  database does not hold.
- **Outbox processing is single-writer.** `DisableConcurrentExecution` keeps one worker on the
  queue. Running more than one instance needs a row-level claim added to `OutboxProcessor`.
- **`web/` targets the previous API.** The Angular workspace in `web/` and the `openapi.json`
  at the repository root describe the archived .NET 9 design, whose endpoints this rebuild does
  not yet have. Regenerate its client once the modules grow endpoints; until then the two are
  intentionally out of step.
