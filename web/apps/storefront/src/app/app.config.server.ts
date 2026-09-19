import { mergeApplicationConfig, ApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { normalizeRootUrl, provideApiConfiguration } from '@upbazaar/data-access';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

/**
 * Where the build reaches the Catalog API while prerendering.
 *
 * In the browser an empty base is right: the dev-server proxy, or IIS in production, puts the API
 * on the same origin. The prerenderer runs in Node on the build machine and has no page to be
 * relative to, so it needs an absolute origin or every fetch fails with ENOTFOUND.
 *
 * The API must be running when the storefront is built. That is deliberate: a build that silently
 * produced an empty catalogue would deploy a shop with nothing in it.
 */
const apiOrigin = process.env['PRERENDER_API_ORIGIN'] ?? 'http://localhost:5199';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    provideApiConfiguration(normalizeRootUrl(apiOrigin)),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
