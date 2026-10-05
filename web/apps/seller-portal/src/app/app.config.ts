import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { AUTH_SESSION_KEY, authInterceptor, providePortalPaginatorIntl } from '@upbazaar/auth';
import { provideDataAccess } from '@upbazaar/data-access';
import { provideDocumentTitle, provideI18n, provideInitialLanguage } from '@upbazaar/ui';
import { appRoutes } from './app.routes';
import { translations } from './i18n/translations';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    // Its own session, so sharing a domain with the other apps does not mean sharing a sign-in.
    { provide: AUTH_SESSION_KEY, useValue: 'upbazaar.seller.session' },
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes, withComponentInputBinding()),
    provideDataAccess({ interceptors: [authInterceptor] }),
    provideI18n(translations),
    provideInitialLanguage(),
    providePortalPaginatorIntl(),
    provideDocumentTitle('app.sellerPortal'),
  ],
};
