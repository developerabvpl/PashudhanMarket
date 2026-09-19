# UP Bazaar storefront — deployment bundle

Built from the UP Bazaar workspace by `tools/scripts/package-storefront.mjs`. Self-contained:
copy the whole folder to the server. Nothing in it runs — it is a folder of static files.

## What is in here

    index.html            the application shell
    products/             one prerendered folder per product, plus the listing
    *.js, *.css           the application, filenames content-hashed
    media/                product photographs and other assets
    web.config            IIS site root: default document, two rewrite rules, cache headers
    nginx.conf.example    the same routing for nginx
    README.md             this file

There is no `server/` folder. Every published product was prerendered into HTML from the
Catalog API when this bundle was built, so crawlers read real product pages. Shoppers then get
live prices and stock: the page fetches them from `/api` as it loads.

## Hosting on IIS

**1. Install once on the server**

- [URL Rewrite](https://www.iis.net/downloads/microsoft/url-rewrite) (MSI, ~1 MB) — usually
  already present. In IIS Manager, select the site: if you see a **URL Rewrite** icon, you have it.
- [Application Request Routing](https://www.iis.net/downloads/microsoft/application-request-routing)
  (MSI). Then, at the **server** node in IIS Manager: *Application Request Routing Cache* →
  *Server Proxy Settings* → tick **Enable proxy** → Apply.

Node is not required on the server. Neither is HttpPlatformHandler.

**2. Run the .NET API** somewhere this server can reach. `web.config` assumes
`http://localhost:5199`; if it is elsewhere, change the URL in the rule named `API`.

**3. Copy this folder** to somewhere like `C:\inetpub\upbazaar`.

**4. Create the site** with that folder as its physical path, bound to your hostname.

`web.config` supplies the rest.

### What the rewrite rules do

**The API.** Anything under `/api` is forwarded to the .NET API unchanged. The browser calls the
API on this origin for live prices, stock, sign-in and the account pages, so without this rule the
product pages paint correctly and then empty as they load.

**Prerendered pages.** `/products/<id>` is a folder holding `index.html`. Asked for without a
trailing slash, IIS would answer with a redirect to add one — visible to crawlers, and two
requests where one would do. The first rule serves the file directly instead.

**Client-rendered pages.** `/sign-in`, `/account` and `/cart` have no file on disk because they
are rendered in the browser by design. The second rule hands them the shell so that a refresh, a
bookmark or a shared link works. Requests that look like an asset are excluded, so a file that
failed to deploy answers an honest 404 instead of HTML that the browser cannot parse.

## Verifying a deployment

    curl -s -o /dev/null -w "%{http_code}\n" https://<host>/products

Then open `view-source:https://<host>/products` and look for product names in the HTML. If they
are there, the prerendered pages are being served and crawlers can read the catalogue. Check a
product page the same way — its `<title>` should be the product's own name, not "storefront".

Deep links matter as much as the home page: load `/products/<some id>` directly in a fresh tab
rather than clicking through to it, since clicking is client-side routing and proves nothing
about the server.

Then check the live half: `https://<host>/api/v1/catalog/categories` should return JSON, not
the storefront's HTML.

## Updating the catalogue

Products are changed through the API (`/api/v1/admin/catalog`), and shoppers see a new price or
stock level at once, without a redeploy. What does lag is the HTML crawlers read: it carries
prices as of the last build, and a newly published product has no prerendered page until the
next one. Rebuild on a schedule, or after a batch of changes, with the API reachable:

    PRERENDER_API_ORIGIN=http://<api host>:5199 npx nx build storefront
    node tools/scripts/package-storefront.mjs

If crawler-visible prices ever have to be exact, the two product routes in
`app.routes.server.ts` go to `RenderMode.Server` and the host needs a Node process again.

## Hosting behind nginx instead

`nginx.conf.example` carries the same two routing rules. The site is static there too — `root`
at this folder and a `try_files` fallback is the whole configuration.

## If something looks wrong

| Symptom | Cause |
| --- | --- |
| Home page loads, every other URL 404s | URL Rewrite is missing, or the site's physical path is a subfolder |
| Page renders in a browser but `view-source` is empty | The shell is being served instead of the prerendered file — check the prerendered-page rule |
| Product page paints, then empties or jumps to the listing | `/api` is not reaching the API: ARR missing, proxy not enabled, or the API is down |
| `/api/...` answers 404 or HTML | ARR is not installed or its proxy is not enabled at server level |
| New product works in a browser but `view-source` is empty | Expected until the next build; it is rendered in the browser meanwhile |
| Styles missing, console shows a parse error | An asset did not deploy and the fallback rule is answering it with HTML |
