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
node tools/scripts/import-catalog.mjs                 # tools/data/*.xlsx -> catalog.json
node tools/scripts/import-catalog.mjs --source other.xlsx
```

Ids are UUIDv5 over a fixed namespace, so re-importing updates the catalogue in place instead of
duplicating it and existing product URLs keep resolving. SKUs are `UPB-<category>-<n>`, and the
category segment is what `ProductThumb` keys its colour on.

**The workbook has no prices, no stock and no photographs**, and the importer invents none. An
imported product carries `price: 0`, which the storefront renders as *Price on request* with the
Add to cart button disabled, and no `Offer` node reaches the JSON-LD — publishing `price: 0`
would advertise the product as free in a search result. Product tiles are a generated wash and
monogram rather than an `<img>` at a path that does not exist. Fill in prices and stock, and the
existing priced path takes over with no code change.

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

The dark palette is a plain `:root` override inside `@media (prefers-color-scheme: dark)`, not a
nested `@theme`. Tailwind hoists every `@theme` block to the top level and emits its variables
unconditionally, so a `@theme` inside a media query is not scoped by it — it simply overwrites
the light values for everyone. That is worth knowing before adding a second theme.

## Known gaps

- **Language switching reloads the storefront.** Its pages are server-rendered, so the language
  has to be applied on the server; the two SPAs switch in place. See `reloadOnSwitch` on
  `LanguageSwitcher`.
- **No cart or checkout in the storefront.** The Add to cart button is inert; the API's checkout
  endpoint is generated and ready to call.
- **No product photographs.** Tiles are a generated wash and monogram; the API has no image
  field and the import had no images. See `ProductThumb`.
- **No prices or stock on the imported catalogue.** Everything reads *Price on request* until a
  seller fills them in. See *The catalogue* above.
- **Catalog, Orders and Payments are stubbed.** Those endpoints come from the archived contract
  merged into the generated client, and the storefront's server render reads them from
  `tools/scripts/stub-api.mjs` on port 5200 (override with `SSR_API_ORIGIN`). Identity is real.
- **Password reset stops at the request.** `/auth/forgot-password` is wired and the API emails a
  token, but there is no page yet that consumes the link.
- **The storefront is Tailwind-only.** Material is used by the two portals; introducing it into
  the storefront would cost the bundle savings the split currently buys.
