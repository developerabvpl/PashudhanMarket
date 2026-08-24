# UP Bazaar Web

Nx workspace holding the three UP Bazaar front ends. Angular 22 standalone + signals +
zoneless, Tailwind 4, Transloco for en/hi, Vitest and Playwright.

## Running

```bash
npm run start:storefront   # SSR, http://localhost:4200
npm run start:seller       # SPA, http://localhost:4201
npm run start:admin        # SPA, http://localhost:4202
```

Each dev server proxies `/api` to the API on `http://localhost:5199`, so run it from the parent
folder alongside them:

```bash
dotnet run --project ../src/UPBazaar.Api --urls http://localhost:5199
```

Same-origin is deliberate: the bearer token is only ever attached to `/api/**` on this origin.

The storefront additionally proxies `/api/catalog` to `http://localhost:5200`, where
`tools/scripts/stub-api.mjs` stands in for the Catalog module until it ships. Two origins is a
transitional arrangement, not a design — see *The API client* below.

```bash
node tools/scripts/stub-api.mjs
```

To sign in as an administrator the API needs a seeded account, which it creates on first run
from environment variables and never writes to a settings file:

```bash
UPBAZAAR_Identity__SuperAdmin__Email=admin@upbazaar.test UPBAZAAR_Identity__SuperAdmin__Password='<choose one>' dotnet run --project ../src/UPBazaar.Api --urls http://localhost:5199
```

Buyer OTP codes are not sent anywhere in development: the fake `ISmsSender` writes them to the
API log at `src/UPBazaar.Api/logs/`.

## Deploying

`nx build storefront` produces a Node SSR server under `dist/apps/storefront`. Three things it
needs that the dev server hides:

**A host allowlist.** Angular rejects any request whose `Host` header is not listed, and the
default list is empty — a fresh production build answers 400 to every request. `localhost` is
set in `project.json` for local runs; add the real domain at run time:

```bash
NG_ALLOWED_HOSTS=upbazaar.example,www.upbazaar.example SSR_API_ORIGIN=http://127.0.0.1:5200 PORT=4000 node dist/apps/storefront/server/server.mjs
```

Or let `package-storefront` assemble the whole thing, stub and data included, into one directory
that can be copied to the server as it stands:

```bash
npx nx build storefront
node tools/scripts/package-storefront.mjs      # -> dist/deploy
```

Never set it to `*` unless a proxy in front is already validating the header.

**A reverse proxy.** The SSR server does not proxy `/api`, and the browser calls the API on its
own origin. Without something in front routing `/api/**` to the API, the first paint is correct
and then the page empties as soon as it hydrates and refetches. One origin, three upstreams:

| Path | Upstream |
| --- | --- |
| `/api/catalog/**` | the catalogue stub on 5200 |
| `/api/**` | the .NET API on 5199 |
| everything else | the SSR server |

`SSR_API_ORIGIN` is separate and internal: it is how Node reaches the catalogue during the server
render, where there is no page to be relative to.

**The stub's data.** `tools/scripts/stub-api.mjs` reads `tools/data/catalog.json` relative to the
workspace root, and neither is part of `dist`. Copy `tools/` alongside the build, or the stub
will not start.

### What runs without a database

The storefront does, completely: browsing, search, category filters, product pages and the
basket all come from the catalogue stub and localStorage. Verified with the API stopped.

The .NET API does not. It applies EF migrations and initialises Hangfire's SQL storage at
startup, and with an unreachable database it never reaches `app.Run()` — so sign-in, OTP, the
account page and the two portals are gone with it. The storefront degrades rather than breaks:
the sign-in sheet reports a failure instead of crashing the page.

## Verifying

```bash
npm run verify                       # lint + unit tests + build, every project
npx nx run-many -t e2e               # all three Playwright smoke suites
npx nx e2e storefront-e2e            # one suite
```

The e2e suites start their own dev servers. The storefront suite also starts
`tools/scripts/stub-api.mjs`, because the storefront renders on the server: its data fetch
happens in Node where Playwright's `page.route()` cannot reach, so browser-side mocking would
leave the server-rendered HTML — the part search engines see — untested. The two SPA suites
mock in the browser, which is sufficient there.

Playwright needs its browser once per machine: `npx playwright install chromium`.

## The API client

`libs/data-access/src/lib/api` is generated and must never be hand-edited.

```bash
npm run gen:api                                  # from a running API on :5199
npm run gen:api -- --url http://localhost:5199   # the same, said explicitly
npm run gen:api -- --offline                     # from the last downloaded document
```

**Transitional: the contract is a merge of two sources.** The live API does not serve Catalog,
Orders or Payments yet, but the storefront, seller and admin features that call them already
exist. So `tools/scripts/generate-api-client.mjs` overlays the archived contract onto the live
one — live wins on any path or schema present in both, archived fills the gaps — and generates
from the result, reporting how many paths it carried over. Delete the archived document and the
overlay together on the day those modules ship; the generated code will shrink and any feature
still calling a phantom endpoint will fail to compile, which is the point.

The generator is `ng-openapi-gen`, chosen because it emits `HttpClient` calls. A fetch-based
client would bypass Angular's interceptors, and both the bearer token and the global error
toast are interceptors.

Calls go through the generated `Api` helper:

```ts
const api = inject(Api);
const product = await api.invoke(catalogGetProduct, { productId });
```

## The catalogue

The storefront's products come from `tools/data/catalog.json`, generated from the supplied
workbook and served by the stub:

```bash
node tools/scripts/import-catalog.mjs                  # tools/data/*.xlsx -> catalog.json
node tools/scripts/import-catalog.mjs --source other.xlsx
node tools/scripts/import-catalog.mjs --no-estimates   # leave unpriced rather than estimate
```

Ids are UUIDv5 over a fixed namespace, so re-importing updates the catalogue in place instead of
duplicating it and existing product URLs keep resolving. SKUs are `UPB-<category>-<n>`, and the
category segment is what `ProductThumb` keys its colour on.

### Prices are indicative until someone supplies real ones

**The workbook has no price, stock or image columns.** Photographs stay absent — tiles are a
generated wash and monogram rather than an `<img>` at a path that does not exist. Prices and
stock are filled in, and it matters where from:

| Source | When | `priceSource` |
| --- | --- | --- |
| `tools/data/prices.csv` | A row exists for that SKU | `supplied` |
| `tools/scripts/pricing.mjs` | Otherwise | `estimated` |
| Nothing | `--no-estimates` was passed | `none` |

**Nothing marked `estimated` is a real supplier price.** Those figures are derived from the pack
size and weight stated in each listing title against a per-category rate, so a 500ml ark costs
about twice a 250ml one and a pack of six soaps costs more than a bar. They are consistent and
plausible, which is what design and demo work needs; they are not researched. The importer says
so on every run, and `catalog.json` carries a `priceWarning` saying so too.

To replace them, add rows to `tools/data/prices.csv` and re-import:

```csv
sku,price,stock
UPB-AGB-001,180,120
UPB-ARK-006,225,
UPB-GHN-004,0,0
```

A price of `0` is not free — it means "not for sale yet", and the storefront renders it as *Price
on request* with the cart button disabled and **no `Offer` node in the JSON-LD**, because
publishing `price: 0` would advertise the product as free in a search result.

### Product photographs

They live in `apps/storefront/public/media/products`. **Not** under `public/products`: that
folder shadows the `/products` route, and the static middleware answers it with a redirect
before the router ever sees it.

Photography is per category, not per product: all ten gomutra ark listings show the same bottle.
`CATEGORY_PHOTOS` in `product-thumb.ts` maps the SKU's middle segment to a filename, and
`TYPE_PHOTOS` overrides it for product types within a category that have their own picture —
dhoop sticks and bamboo-cored agarbatti are different products and the catalogue has a photograph
of each. Filenames are listed rather than derived, so a supplied file keeps its own name.

A category with no entry, or an entry whose file fails to load, falls back to a drawn tile rather
than a broken-image icon, and its JSON-LD carries no `image`. Gomutra Ghanvati has no photograph
and does exactly that.

Tiles are square and use `object-contain`, so a portrait source letterboxes rather than losing
its subject to a crop. The thumbnail is positioned absolutely inside that square on purpose: an
in-flow `<img>` contributes its intrinsic height and overrides the box's `aspect-ratio`, which
is how one 182x500 bottle stretched its cards to two and a half times the height of the rest.

### The basket

`CartStore` holds it: signals for the lines, the count and the subtotal, persisted to
localStorage under one key. The header badge reads `count`, and because the badge is decorative
the count also goes into the link's `aria-label`.

A product with no price is refused rather than added at zero — the catalogue carries listings
whose seller has not set one, and a basket that totals them as free is worse than a button that
does nothing. Restored lines are validated on the way in, so a hand-edited or half-written entry
is dropped instead of turning the subtotal into `NaN`.

The Add to cart button sits on a server-rendered page, so a click can land before hydration.
`withEventReplay()` is what catches it and applies it once Angular takes over — there is a smoke
test that clicks at `waitUntil: 'commit'` to keep that honest.

## Layout

```
apps/
  storefront          public catalogue, SSR, SEO + JSON-LD
  seller-portal       SPA, product creation with Signal Forms
  admin-portal        SPA, staff users and order lookup
  *-e2e               Playwright smoke suites, tagged @smoke
libs/
  data-access         generated client + provider, problem-details mapping, error interceptor
  auth                token + profile stores, AuthService, interceptor, guards, portal pages
  ui                  theme tokens, i18n, toast, field errors, page states, language switcher
  util                India-format validators (GSTIN/PAN/IFSC/PIN/mobile), inr and dateIst pipes
```

Dependencies run one way: `util <- ui <- data-access <- auth <- apps`. Auth sits above
data-access because it calls the generated client to sign in, refresh and load the current user;
data-access knows nothing of auth, and each app hands it `authInterceptor` at its composition
root. That ordering is enforced by
`@nx/enforce-module-boundaries` against the `type:*` tag on each project, so a reach sideways
or upwards fails lint rather than review.

## How the pieces fit

**State** is signals throughout — the auth stores, `ToastService` and the feature components all
expose readonly signals and derive with `computed`. No component injects `HttpClient`.

**Auth** is split in two on purpose. `AuthTokenStore` holds nothing but the tokens and their
expiry; `CurrentUserStore` loads `/users/me` and exposes `user`, `roles` and `permissions`.
Permissions come from that call rather than from decoding the JWT, so a token minted before a
role change cannot silently grant what the server would refuse — and the client never has to
trust a token it did not verify.

`AuthService` is the only thing the screens talk to: `requestOtp`, `verifyOtp`, `login`,
`verifyTwoFactor`, `register`, `refresh`, `logout`, and the password and TOTP calls.

`authInterceptor` attaches the token to same-origin `/api` calls, skipping an explicit
anonymous list so a login request never carries a stale bearer. On a 401 it refreshes once and
retries, and concurrent 401s share one refresh through a single-flight coordinator — the server
treats a replayed refresh token as theft and revokes the whole family, so two parallel refreshes
would sign the user out. A 403 is deliberately left alone: the user is signed in and simply
lacks the permission.

Routes use `authGuard`, which awaits the profile load before deciding, and
`permissionGuard('catalog.products.write')`, which routes to `/forbidden` naming what was
required. `*hasPermission` hides affordances the API would reject anyway.

**Sign-in surfaces differ by audience.** Buyers get a CDK bottom sheet (and an equivalent page)
that leads with mobile OTP — number, then code, with a 30-second resend timer — and keeps
email/password as a second tab, because a buyer on a phone has a number and rarely a password.
Staff and sellers get Material email/password pages from `libs/auth/src/lib/portal`, plus
forgot-password, change-password and a TOTP challenge screen. The storefront never imports those
pages, which is what keeps Material out of its bundle.

**Errors.** `toApiProblem` normalises any failure — including a network drop — into a code, a
title and per-field messages. `httpErrorInterceptor` raises a toast for everything except
validation errors, which belong under the offending input, and 401s, which the auth
interceptor is already handling. Forms merge their own validation messages with the API's for
the same field.

**i18n.** Transloco with `en.json` / `hi.json` bundled rather than fetched, so the server render
already has the strings. The chosen language is stored in a cookie and applied before the first
render by `provideInitialLanguage()` — on the server it comes off the request, which is what
lets the storefront emit Hindi HTML to a crawler.

**SEO.** Every storefront page calls `SeoService.apply()` for title, description, canonical and
Open Graph; product pages add Product and BreadcrumbList JSON-LD. Product data is loaded in a
route resolver rather than in the component, because the router awaits resolvers during SSR and
that is what puts the content in the served HTML.

**Styling.** Tailwind 4 with tokens in `libs/ui/src/lib/theme/tokens.css`. Colours are OKLCH
custom properties; components use utilities such as `bg-surface` and `text-ink` and never an
inline hex.

**The storefront is light-themed and does not follow the operating system.** The dark palette
still exists, behind `:root[data-theme="dark"]`, but nothing sets that attribute; `color-scheme`
is pinned to `light` so scrollbars and form controls match. Wire the attribute to a user setting
to bring dark back.

Whatever you do, do not put those overrides in a nested `@theme`. Tailwind hoists every `@theme`
block to the top level and emits its variables unconditionally, so a `@theme` inside a media
query or a selector is not scoped by it — it overwrites the light values for everyone. That bug
is why the storefront rendered dark for every visitor for as long as it did.

## Known gaps

- **Language switching reloads the storefront.** Its pages are server-rendered, so the language
  has to be applied on the server; the two SPAs switch in place. See `reloadOnSwitch` on
  `LanguageSwitcher`.
- **The basket is client-side and there is no checkout.** `CartStore` keeps it in localStorage,
  so it survives a reload but not a change of device, and the checkout button is disabled with a
  reason. Cart and Ordering do not exist in the API yet; when they do, the store is the only
  thing that needs a server behind it.
- **Photography is per category, not per product.** All eight agarbatti listings share
  `agb.jpg`. Categories with no file, and any file that fails to load, fall back to a drawn tile.
  See `apps/storefront/public/media/products/README.md`.
- **Prices and stock are estimates, not supplier figures.** Every imported listing is priced by
  `tools/scripts/pricing.mjs`. Replace them through `tools/data/prices.csv` before anyone treats
  the storefront as a price list. See *The catalogue* above.
- **Catalog, Orders and Payments are stubbed.** Those endpoints come from the archived contract
  merged into the generated client, and the storefront's server render reads them from
  `tools/scripts/stub-api.mjs` on port 5200 (override with `SSR_API_ORIGIN`). Identity is real.

  **There is no product table.** The database holds Identity (`identity.*`), the shared audit
  and outbox tables (`shared.*`) and Hangfire's own — all of them live and in use. Products are
  `tools/data/catalog.json` and nothing else, so do not go looking for them in SQL, and do not
  read "the catalogue is a JSON file" as "the database is unused": pulling the connection string
  takes sign-in, OTP, staff admin, the audit log and the job dashboard down with it.
- **Password reset stops at the request.** `/auth/forgot-password` is wired and the API emails a
  token, but there is no page yet that consumes the link.
- **The storefront is Tailwind-only.** Material is used by the two portals; introducing it into
  the storefront would cost the bundle savings the split currently buys.
