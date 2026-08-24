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
 */
const apiOrigin = process.env['API_ORIGIN'] ?? 'http://localhost:5199';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    provideApiConfiguration(normalizeRootUrl(apiOrigin)),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
