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
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideDataAccess } from '@upbazaar/data-access';
import { provideI18n, provideInitialLanguage } from '@upbazaar/ui';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideClientHydration(withEventReplay()),
    provideRouter(
      appRoutes,
      // Route resolvers feed page data straight into component inputs, which is what puts
      // product content in the server-rendered HTML rather than after hydration.
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' })
    ),
    // Same origin: the dev server proxies /api to the .NET API, so the bearer token never
    // leaves it. The server render overrides this with an absolute origin.
    provideDataAccess(),
    provideI18n(),
    provideInitialLanguage(),
  ],
};
