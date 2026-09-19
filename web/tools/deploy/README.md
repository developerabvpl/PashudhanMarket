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

There is no `server/` folder and no catalogue service. The seventy products are compiled into
the bundle and prerendered into HTML at build time, so the server has nothing to execute.

## Hosting on IIS

**1. Install once on the server**

[URL Rewrite](https://www.iis.net/downloads/microsoft/url-rewrite) (MSI, ~1 MB) — usually already
present. In IIS Manager, select the site: if you see a **URL Rewrite** icon, you have it.

Node is not required on the server. Neither is HttpPlatformHandler.

**2. Copy this folder** to somewhere like `C:\inetpub\upbazaar`.

**3. Create the site** with that folder as its physical path, bound to your hostname.

That is the whole deployment. `web.config` supplies the rest.

### What the rewrite rules do

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

## Updating the catalogue

The products live in `tools/data/catalog.json` in the workspace, not on the server. Changing a
price or adding a product means editing that file, rebuilding and redeploying:

    npx nx build storefront
    node tools/scripts/package-storefront.mjs

This is the trade the static build makes. It is the right one while the catalogue is a file that
only changes when someone edits it, and the wrong one the moment stock and prices move on their
own. When the Catalog module ships in the API, the two product routes in `app.routes.server.ts`
go back to `RenderMode.Server`, `catalog.source.ts` starts calling the API instead of importing
the JSON, and the host needs a Node process again.

## Hosting behind nginx instead

`nginx.conf.example` carries the same two routing rules. The site is static there too — `root`
at this folder and a `try_files` fallback is the whole configuration.

## If something looks wrong

| Symptom | Cause |
| --- | --- |
| Home page loads, every other URL 404s | URL Rewrite is missing, or the site's physical path is a subfolder |
| Page renders in a browser but `view-source` is empty | The shell is being served instead of the prerendered file — check the first rewrite rule |
| A product added to `catalog.json` is not on the site | The bundle was not rebuilt; prerendering happens at build time |
| Styles missing, console shows a parse error | An asset did not deploy and the fallback rule is answering it with HTML |
