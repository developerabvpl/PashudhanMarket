import { mergeApplicationConfig, ApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import {
  normalizeRootUrl,
  provideApiConfiguration,
} from '@upbazaar/data-access';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

/**
 * In the browser an empty base is right: the dev-server proxy or the reverse proxy in
 * front of production puts the API on the same origin. Node has no page to be relative to, so
 * the server render needs an absolute origin or every fetch fails with ENOTFOUND.
 *
 * TRANSITIONAL: the only thing rendered on the server is the catalogue, and the Catalog module
 * does not exist in the real API yet, so this points at tools/scripts/stub-api.mjs. Everything
 * tied to a signed-in user is client-rendered and reaches the real API through the proxy,
 * which is why a single origin suffices here. Point this back at the API and delete the stub
 * the day Catalog ships.
 */
const apiOrigin = process.env['SSR_API_ORIGIN'] ?? 'http://localhost:5200';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    provideApiConfiguration(normalizeRootUrl(apiOrigin)),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
