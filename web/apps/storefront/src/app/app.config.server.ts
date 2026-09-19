import { mergeApplicationConfig, ApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

/**
 * Nothing rendered off the browser makes an HTTP call any more.
 *
 * The catalogue is imported from the bundle, and every page that talks to the API is
 * client-rendered by app.routes.server.ts. That removes the absolute API origin this file used
 * to need: prerendering happens on a build machine where no server is listening, so an origin
 * would have been a value that could only ever be wrong.
 *
 * Restore provideApiConfiguration here if a route ever needs data at prerender time again.
 */
const serverConfig: ApplicationConfig = {
  providers: [provideServerRendering(withRoutes(serverRoutes))],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
