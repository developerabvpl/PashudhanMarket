import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Product data changes whenever a seller edits a listing or stock moves, so product pages are
 * rendered per request rather than prerendered: a crawler must never be served a cached price
 * or an "in stock" badge for something that sold out an hour ago.
 *
 * Anything tied to a signed-in user renders on the client only. Server-rendering it would
 * either leak one visitor's details into a shared cache or, at best, render a signed-out shell
 * that the client immediately replaces.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'products', renderMode: RenderMode.Server },
  { path: 'products/:productId', renderMode: RenderMode.Server },
  { path: 'cart', renderMode: RenderMode.Client },
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'account/**', renderMode: RenderMode.Client },
  { path: 'sign-in', renderMode: RenderMode.Client },
  { path: 'forbidden', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Prerender },
];
