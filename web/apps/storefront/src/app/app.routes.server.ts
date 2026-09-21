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
