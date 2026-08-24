# UP Bazaar storefront — deployment bundle

Built from the UP Bazaar workspace by `tools/scripts/package-storefront.mjs`. Self-contained:
copy the whole folder to the server and run it.

## What is in here

    browser/              static assets, including the product photographs
    server/               the Angular SSR server (Node, Express)
    api/catalog/          the catalogue stub, laid out to match the URL it answers on
    tools/data/           catalog.json, the 70 products it serves
    web.config            IIS site root
    api/catalog/web.config  IIS application for the catalogue
    nginx.conf.example    the same routing for nginx
    start.sh              run both by hand (Linux, macOS)
    start.cmd             run both by hand (Windows)

## Hosting on IIS

IIS does not run JavaScript, so it has to start Node for you. The **HttpPlatformHandler** module
does exactly that: it launches the process, hands it a private port and forwards every request to
it, restarting it if it dies. No Windows service to register, no ARR.

**1. Install once on the server**

- [Node.js LTS](https://nodejs.org) — confirm with `node --version` in a fresh shell
- [HttpPlatformHandler](https://www.iis.net/downloads/microsoft/httpplatformhandler) (MSI, ~1 MB)

**2. Copy this folder** to somewhere like `C:\inetpub\upbazaar`.

**3. Create the site** with that folder as its physical path, bound to your hostname.

**4. Turn the catalogue folder into an application**

In IIS Manager, expand the site, right-click the `catalog` folder inside `api`, and choose
**Convert to Application**. Accept the alias it offers — `catalog` — and press OK.

That lands the catalogue at `/api/catalog`, which is the URL the storefront calls. The folders
are laid out to match the URL for exactly this reason: IIS Manager only accepts a single path
segment as an alias, so typing `api/catalog` into the Add Application dialog is rejected. Let
the folder structure carry the nesting and the alias stays one word.

From a command prompt instead, if you prefer:

```
appcmd add app /site.name:"upbazaar" /path:/api/catalog ^
               /physicalPath:"C:\inetpub\upbazaar\api\catalog"
```

Both `web.config` files are already in place: the site root runs the storefront, this
application runs the catalogue.

**5. Put your hostnames in `NG_ALLOWED_HOSTS`** in the root `web.config`. This is not optional.
Angular refuses any request whose `Host` header is not listed, and what ships here is an example —
leave it and the site answers 400 to everyone. Do not use `*`.

**6. Grant the application pool identity** (`IIS AppPool\<pool name>`) read access to the folder,
and **write** access to `logs\`, which is where Node's output lands when a process fails to start.

### When it does not come up

| Symptom | Cause |
| --- | --- |
| 400, `Header "host" ... is not allowed` | `NG_ALLOWED_HOSTS` still holds the example hostnames |
| 502.5 | Node is not on PATH for the pool identity — put the full path to `node.exe` in `processPath` |
| Page paints, then goes empty | The `api/catalog` application is missing, so the browser gets HTML where it expects JSON |
| Catalogue empty from the first paint | `SSR_API_ORIGIN` cannot reach the site; if it is not on port 80, set the real origin |
| 403 on every path | Only `browser\` was published, as a static site — see below |

### Why publishing browser\ on its own gives a 403

There is no `index.html` in it, only `index.csr.html`, so IIS finds no default document and
directory browsing is off. That is not a bug to work around. This is a server-rendered
application: `index.csr.html` is the shell the Node server uses for client-rendered routes, not a
page to publish. Renaming it clears the 403 and leaves an empty catalogue, because the products
come from the stub, which is a Node process.

## Hosting behind nginx

`nginx.conf.example` carries the same routing. One public origin, three upstreams: `/api/catalog`
to the stub, `/api` to the .NET API, everything else to the SSR server.

## Running it by hand

    NG_ALLOWED_HOSTS=upbazaar.example ./start.sh          # Linux, macOS
    set NG_ALLOWED_HOSTS=upbazaar.example && start.cmd    # Windows

Run them **from this folder** — the paths inside are relative to it, and starting them from
anywhere else fails to find `api/catalog/stub-api.mjs`.
`PORT` defaults to 4000 and `STUB_API_PORT` to 5200. A reverse proxy still has to sit in front:
the SSR server does not proxy `/api`, and the browser calls the API on its own origin.

## The .NET API is optional, and it needs SQL Server

Everything a shopper does — browsing, search, category filters, product pages, the basket — comes
from the catalogue stub and the browser's own storage, and works with no API and no database at
all. Only sign-in needs the API, and the API does not start without its database: it applies
migrations and initialises Hangfire's SQL storage before it listens. Leave `/api/**` unrouted and
the storefront still runs; the sign-in panel reports a failure instead of breaking the page.

Add it as an application at alias `api`, with its own publish folder as the physical path.
The `catalog` application underneath it keeps working: IIS matches the longest application
path, so `/api/catalog` still reaches the stub while everything else under `/api` goes to the
API. The empty `api` folder in this bundle exists only so that the alias in step 4 could be a
single word.

## Prices are indicative

The catalogue was imported from a listing survey with no price column. Every price in
`tools/data/catalog.json` is an estimate derived from the pack size in the listing title, not a
supplier price. Replace them before anyone treats this as a price list.
