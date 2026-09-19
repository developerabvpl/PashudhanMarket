import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import {
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import {
  provideClientHydration,
  withEventReplay,
  withHttpTransferCacheOptions,
} from '@angular/platform-browser';
import { authInterceptor } from '@upbazaar/auth';
import { provideDataAccess } from '@upbazaar/data-access';
import { provideI18n, provideInitialLanguage } from '@upbazaar/ui';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    // Catalogue responses stay out of the transfer cache. The HTML was prerendered at build time,
    // so replaying its responses would show build-time prices and stock; leaving them out makes
    // the browser's own resolver run fetch live data instead.
    provideClientHydration(
      withEventReplay(),
      withHttpTransferCacheOptions({ filter: (req) => !req.url.includes('/api/v1/catalog/') })
    ),
    provideRouter(
      appRoutes,
      // Route resolvers feed page data straight into component inputs, which is what puts
      // product content in the server-rendered HTML rather than after hydration.
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' })
    ),
    // Same origin: the dev server proxies /api to the .NET API, so the bearer token never
    // leaves it. The prerenderer overrides this with an absolute origin.
    provideDataAccess({ interceptors: [authInterceptor] }),
    provideI18n(),
    provideInitialLanguage(),
  ],
};
