import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Product data changes whenever a seller edits a listing or stock moves, so product pages are
 * rendered per request rather than prerendered: a crawler must never be served a cached price
 * or an "in stock" badge for something that sold out an hour ago.
 *
 * Everything else is static shell and can be prerendered at build time.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'products', renderMode: RenderMode.Server },
  { path: 'products/:productId', renderMode: RenderMode.Server },
  { path: 'sign-in', renderMode: RenderMode.Client },
  { path: 'forbidden', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Prerender },
];
