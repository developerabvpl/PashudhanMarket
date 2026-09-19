import { RenderMode, ServerRoute } from '@angular/ssr';
import { allProductIds } from './features/products/catalog.source';

/**
 * The catalogue is prerendered, not server-rendered.
 *
 * Server rendering existed so a crawler could never be handed a stale price. That reasoning held
 * while the data came from an API; it does not hold now, because the catalogue is a file in the
 * bundle and cannot change between a build and the next build. Prerendering turns every product
 * into a real HTML file that any web server can hand over on its own, which is what lets this
 * deploy onto IIS with no Node process and no extra module.
 *
 * The day the Catalog module ships and prices move at runtime, these two routes go back to
 * RenderMode.Server and the host needs a Node process again. That is the trade being made here,
 * and it is written down so the next person does not have to infer it.
 *
 * Anything tied to a signed-in user stays client-only. Prerendering it would ship a signed-out
 * shell that the client immediately replaces, and those pages must not be indexed anyway.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'products', renderMode: RenderMode.Prerender },
  {
    path: 'products/:productId',
    renderMode: RenderMode.Prerender,
    getPrerenderParams: () =>
      Promise.resolve(allProductIds().map((productId) => ({ productId }))),
  },
  { path: 'cart', renderMode: RenderMode.Client },
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'account/**', renderMode: RenderMode.Client },
  { path: 'sign-in', renderMode: RenderMode.Client },
  { path: 'forbidden', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Prerender },
];
