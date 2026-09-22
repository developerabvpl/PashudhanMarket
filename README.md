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
responses, and Orders reserves, commits and returns stock through it. A reservation is all-or-nothing,
expires on its own (a Hangfire job sweeps every minute), and is then committed or released.
Concurrent reservations for the last unit are settled by a row version on the stock row, so
stock cannot be oversold.

Cart keeps one cart per signed-in buyer under `/api/v1/cart`; the Buyer role holds
`cart.read` and `cart.write`, and no route takes a cart id, so a buyer can only ever reach their
own. A cart stores products, quantities and the price each had when chosen - nothing else.
Names, current prices and stock are read from Catalog and Inventory on every request, and each
line is flagged `Unavailable`, `InsufficientStock` or `PriceChanged` when it cannot be bought as
shown. Guests keep the storefront's in-browser basket, which `POST /api/v1/cart/merge` folds in
at sign-in. Adding to a cart reserves nothing: stock is held at checkout.

Orders turns a clean cart into an order under `POST /api/v1/orders`. A cart with any problem
line is refused, so nobody is charged for something other than what the cart showed. The order
copies in names, SKUs, prices and the delivery address, and splits itself into one part per
seller, each packed, shipped and cancelled on its own. Checkout reserves the stock, writes the
order and empties the cart in one transaction, and a per-buyer lock stops a double submit
placing two orders. Two ways to pay:

- **Cash on delivery** is confirmed at once and its stock committed.
- **Online** waits in `PendingPayment` with the stock held. Payments confirms it through
  `IOrderPaymentService`, which commits the stock. An order unpaid after
  15 minutes is cancelled by a Hangfire job and its hold released; the hold itself lasts five
  minutes longer, so the order always goes first.

A buyer (`orders.own.read`, `orders.own.write`) sees only their own orders and may cancel until
anything ships. Staff use `/api/v1/admin/orders`: `orders.read` to look, `orders.write` to move a
part to Packed, Shipped or Delivered, and `orders.cancel` to cancel an order or one seller's part.
Cancelling after confirmation puts the stock back as a `Returned` movement. Every change raises
an event (`OrderPlaced`, `OrderConfirmed`, `OrderPartCancelled` with the refund due, and
`OrderCancelled`) for Payments, Shipping (Shiprocket, one shipment per part) and Notifications
to pick up.

Payments takes online payment through Razorpay. The buyer's order page asks
`POST /api/v1/payments/orders/{id}/checkout` for a Razorpay order (created with automatic
capture and reused if they come back) and opens Razorpay Checkout with it. Checkout's result goes
to `POST /api/v1/payments/razorpay/verify`, which checks Razorpay's signature before anything
else, records the money, then confirms the order. The same settlement path serves two safety
nets: the webhook at `/api/v1/payments/webhooks/razorpay` (HMAC-checked over the raw body,
de-duplicated on the event id) for a buyer who pays and then loses signal, and a Hangfire job
that, every minute, finishes any payment captured but not yet applied to its order.

A payment that lands on an order that can no longer take it - cancelled at its deadline, say - is
recorded as owed back in full. So is each seller's part cancelled after payment, from the
`OrderPartCancelled` event. Refunds are made by hand in the Razorpay dashboard for now, and staff
record the Razorpay refund id against them on the admin portal's Payments page
(`payments.read` to see, `payments.refunds.write` to record; Admin and FinanceOfficer hold both).

Razorpay's keys come from configuration and never from appsettings.json:

```bash
dotnet user-secrets --project src/UPBazaar.Api set "Payments:Razorpay:KeyId" "rzp_test_..."
dotnet user-secrets --project src/UPBazaar.Api set "Payments:Razorpay:KeySecret" "..."
dotnet user-secrets --project src/UPBazaar.Api set "Payments:Razorpay:WebhookSecret" "..."
```

Without all three, Development and the tests use a fake gateway that signs exactly as Razorpay
does with a known secret, and the storefront offers a "simulate payment" step instead of the
Razorpay window. Any other environment without keys switches online payment off, and checkout
offers cash on delivery only.

Shipping books couriers through Shiprocket, one consignment per seller's part of an order. Staff
pack a part from the admin portal's order screen (`POST /api/v1/admin/shipping/orders/{id}/parts/{partId}/pack`).
The parcel is worked out from the products' recorded packages - `PUT
/api/v1/admin/catalog/products/{id}/package` sets one unit's weight and box - or entered by hand
when a product has none. Booking is Shiprocket's three steps (order, AWB, pickup), saved after
each, so packing again after a failure resumes rather than books twice. A cash-on-delivery part
is booked as COD for its own subtotal. Then the part is marked Packed.

Couriers collect from the seller's own pickup location if one is set, otherwise from the
platform warehouse (the location with no seller). Only the names are kept here, under the admin
portal's Shipping page; each must match a pickup location registered in the Shiprocket dashboard.

Shiprocket's tracking webhook, at `/api/v1/shipping/webhooks/courier-tracking` and authenticated
by the `x-api-key` token set on it, moves the part to Shipped and Delivered. (Shiprocket refuses
webhook URLs containing its own name, which is why the path does not.) Updates only ever move a
shipment forward, so late and repeated ones are harmless. Cancelling a part before it is
collected cancels its consignment. Buyers see the courier, the AWB and a tracking link on their
order page.

When the courier cannot deliver (RTO), Shiprocket's updates move the shipment to ReturnInTransit
and the order's part to Returning, and - on "RTO DELIVERED" - to Returned. Stock that left at
confirmation does not come back by itself: the seller, in the seller portal, or staff, from the
order screen, inspect the parcel once and record it Good, which puts its stock back as a
`Returned` movement, or Damaged, which does not. For an order paid online, the part's share is
recorded as a refund due only once the parcel is back, since an RTO can be turned round on the
way; cash on delivery collected nothing, so owes nothing. An order where nothing arrived ends
Cancelled, saying it could not be delivered. A late "delivered" never undoes a return, and a late
RTO never undoes a delivery.

Credentials, as with Razorpay, come from user-secrets or the environment:

```bash
dotnet user-secrets --project src/UPBazaar.Api set "Shipping:Shiprocket:Email" "api-user@..."
dotnet user-secrets --project src/UPBazaar.Api set "Shipping:Shiprocket:Password" "..."
dotnet user-secrets --project src/UPBazaar.Api set "Shipping:Shiprocket:WebhookToken" "..."
```

Without them, Development and the tests use a fake courier; any other environment turns courier
booking off, and staff move parts along by hand with `/api/v1/admin/orders/.../status`.

Sellers sign up themselves. A seller creates an account in the seller portal and applies with
shop, address and KYC details (PAN, optional GSTIN checked against it, bank account and IFSC).
Staff with `sellers.kyc.approve` approve or reject from the admin portal's Sellers page; a
rejected applicant sees the note and can resubmit. Approval grants the owner the SellerOwner role,
which the seller portal picks up by refreshing the session. One account runs one shop. A seller's
public id is the seller id products, orders and shipments already carry, and every seller-facing
endpoint (`/api/v1/seller/...`) finds the seller from the caller's token through
`ISellerDirectory`, so no request can name another seller's shop.

An approved seller can:

- create listings as drafts and submit them for review; a moderator publishes them or sends
  them back with a note, from the admin portal's Listings to review page;
- change the price, stock count and package of a live listing without a new review, but not its
  wording or category, which the moderator approved;
- work through their own parts of orders and pack and book couriers for them;
- set their own Shiprocket pickup location.

The sample catalogue's seller (`22222222-...`) is seeded in development as "UP Gaushala
Collective", approved and without an owner; staff link one with
`POST /api/v1/admin/sellers/{id}/owner`.

Deliberately not built yet:

- **Bank account numbers are stored in plain text.** Like the TOTP secrets, they should be
  encrypted at rest before real payouts run. The API only ever returns the last four digits.
- **The seller portal was not tried in a browser past its sign-in page**, since that needs a
  password account; the whole seller flow is covered by the integration tests.
- **The real Shiprocket client is untested against Shiprocket.** It follows Shiprocket's
  published API. Before going live, book one test consignment and point the tracking webhook at
  the API through a tunnel.
- **The existing catalogue has no packages.** None of the 70 products has a weight or box size,
  so until they are measured every parcel is entered by hand at packing. The sample seller can set
  them in the seller portal once staff link an owner to it.
- **Only courier returns (RTO) are handled.** A buyer cannot yet ask to return something that
  was delivered; that needs a return window, reasons and a reverse pickup.
- **The real Razorpay client is untested against Razorpay.** It follows Razorpay's published
  Orders API and signatures, which the tests cover, but there were no keys to run it with. Try a
  test-mode payment and a webhook (through a tunnel such as ngrok) before going live.
- **Refunds are manual.** Payments records what is owed; the money goes back through the
  Razorpay dashboard. Automatic refunds through Razorpay's API can replace that step without the
  refund record changing.
- **Delivery is free.** `ShippingFee` is always zero until Shipping prices a delivery.

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
