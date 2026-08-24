# UP Bazaar Web

Nx workspace holding the three UP Bazaar front ends. Angular 22 standalone + signals +
zoneless, Tailwind 4, Transloco for en/hi, Vitest and Playwright.

## Running

```bash
npm run start:storefront   # SSR, http://localhost:4200
npm run start:seller       # SPA, http://localhost:4201
npm run start:admin        # SPA, http://localhost:4202
```

Each dev server proxies `/api` to `http://localhost:5199`, so run the API from the parent
folder alongside them:

```bash
dotnet run --project ../src/UPBazaar.Api
```

Same-origin is deliberate: the bearer token is only ever attached to `/api/**` on this origin.

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
npm run gen:api                              # from ../openapi.json, committed by the API repo
npm run gen:api -- --url http://localhost:5199   # from a running API, after changing an endpoint
```

The generator is `ng-openapi-gen`, chosen because it emits `HttpClient` calls. A fetch-based
client would bypass Angular's interceptors, and both the bearer token and the global error
toast are interceptors.

Calls go through the generated `Api` helper:

```ts
const api = inject(Api);
const product = await api.invoke(catalogGetProduct, { productId });
```

## Layout

```
apps/
  storefront          public catalogue, SSR, SEO + JSON-LD
  seller-portal       SPA, product creation with Signal Forms
  admin-portal        SPA, order lookup
  *-e2e               Playwright smoke suites, tagged @smoke
libs/
  data-access         generated client + provider, problem-details mapping, error interceptor
  auth                token store, interceptor, guards, *hasPermission, sign-in/forbidden pages
  ui                  theme tokens, i18n, toast, field errors, page states, language switcher
  util                India-format validators (GSTIN/PAN/IFSC/PIN/mobile), inr and dateIst pipes
```

Dependencies run one way: `util <- ui <- auth <- data-access <- apps`. That is enforced by
`@nx/enforce-module-boundaries` against the `type:*` tag on each project, so a reach sideways
or upwards fails lint rather than review.

## How the pieces fit

**State** is signals throughout — `AuthStore`, `ToastService` and the feature components all
expose readonly signals and derive with `computed`. No component injects `HttpClient`.

**Auth.** `AuthStore` reads identity and permissions out of the JWT and persists the session.
`authInterceptor` attaches the token to same-origin `/api` calls and signs out on a 401; a 403
is deliberately left alone, because the user is signed in and simply lacks the permission.
Routes use `authGuard` and `permissionGuard('catalog.products.write')`, and
`*hasPermission` hides affordances the API would reject.

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
custom properties with a dark-scheme block; components use utilities such as `bg-surface` and
`text-ink` and never an inline hex.

## Known gaps

- **Language switching reloads the storefront.** Its pages are server-rendered, so the language
  has to be applied on the server; the two SPAs switch in place. See `reloadOnSwitch` on
  `LanguageSwitcher`.
- **No token issuer.** Sign-in accepts a pasted JWT. Replace the body of `SignInPage.submit`
  when an identity provider exists; the store, interceptor and guards already work off whatever
  token they are handed.
- **No cart or checkout in the storefront.** The Add to cart button is inert; the API's checkout
  endpoint is generated and ready to call.
- **Product images** are placeholder paths keyed on SKU. The API has no image field yet.
- **Angular Material is installed but unused.** The seller and admin screens are Tailwind-only
  so far; Material components can be introduced per screen without further setup.
