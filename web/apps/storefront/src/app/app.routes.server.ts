import { inject } from '@angular/core';
import { RenderMode, ServerRoute } from '@angular/ssr';
import { Api, apiV1CatalogProductsGet } from '@upbazaar/data-access';

/**
 * The catalogue is prerendered from the Catalog API at build time, then refreshed live in the
 * browser.
 *
 * Prerendering turns every published product into a real HTML file that any web server can hand
 * over on its own, which is what lets this deploy onto IIS with no Node process. The cost is that
 * the HTML a crawler reads carries prices and stock as of the last build. A shopper never sees
 * those: the resolvers run again in the browser and fetch live data (see products.resolvers.ts).
 *
 * A product published after the build has no file yet. IIS falls back to the client shell for it
 * and the browser renders it live, so it works at once and gains a prerendered page on the next
 * build. If crawler-visible prices ever have to be exact, these two routes go to
 * RenderMode.Server and the host needs a Node process again.
 *
 * Anything tied to a signed-in user stays client-only. Prerendering it would ship a signed-out
 * shell that the client immediately replaces, and those pages must not be indexed anyway.
 *
 * ## Language
 *
 * A prerendered page is drawn once, at build time, for nobody in particular: it is English, with
 * `<html lang="en">`, whatever the upb.lang cookie of the visitor who later asks for it says. The
 * client-only shell is the same. (`nx serve` renders every route per request, so there a Hindi
 * cookie does get Hindi HTML back - which is why a product page looked right in development while
 * /products did not. The built site has no server to do that.)
 *
 * Rendering these routes per request (RenderMode.Server) would put Hindi in the HTML, and was
 * weighed and turned down: the build is `outputMode: "static"` so that IIS can serve it with no
 * Node process, and that mode does not allow server-rendered routes at all. It would also give up
 * what prerendering is for - a page served from disk, the same for every visitor and cacheable -
 * for the sake of the first paint of returning Hindi visitors. Crawlers send no cookie and read
 * the English page either way.
 *
 * So the pages stay prerendered in English and the browser puts the language right before it
 * paints anything. A few lines at the top of index.html read the cookie while the page is still
 * being parsed: they set `<html lang>` at once - on the client-only routes too, long before the
 * app starts - and, when the page was drawn in another language, keep the app's root hidden until
 * the app has redrawn it (provideInitialLanguage in libs/ui shows it again once the app is stable,
 * and a timer in index.html does regardless). A Hindi visitor therefore sees the page appear in
 * Hindi a moment later than an English visitor sees it in English, instead of English first and
 * Hindi after. English visitors, and anyone without the cookie, are not held back at all.
 *
 * If Hindi pages must ever be in the HTML itself - for a crawler, or a visitor without scripts -
 * the answer is a prerendered Hindi copy under its own URL (/hi/products), not per-request
 * rendering: a cookie cannot choose between two static files.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'products', renderMode: RenderMode.Prerender },
  {
    path: 'products/:productId',
    renderMode: RenderMode.Prerender,
    getPrerenderParams: publishedProductIds,
  },
  { path: 'cart', renderMode: RenderMode.Client },
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'account/**', renderMode: RenderMode.Client },
  { path: 'checkout', renderMode: RenderMode.Client },
  { path: 'orders', renderMode: RenderMode.Client },
  { path: 'orders/**', renderMode: RenderMode.Client },
  { path: 'sign-in', renderMode: RenderMode.Client },
  { path: 'forbidden', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Prerender },
];

/**
 * Every published product, walked a page at a time. Runs in an injection context on the build
 * machine, against the API origin set in app.config.server.ts.
 */
async function publishedProductIds(): Promise<{ productId: string }[]> {
  const api = inject(Api);
  const ids: { productId: string }[] = [];

  for (let page = 1; ; page++) {
    const result = await api.invoke(apiV1CatalogProductsGet, { Page: page, PageSize: 100 });

    ids.push(...result.items.map((product) => ({ productId: product.id })));

    if (!result.hasNextPage) {
      return ids;
    }
  }
}
