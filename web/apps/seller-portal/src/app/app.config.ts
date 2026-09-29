import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor } from '@upbazaar/auth';
import { provideDataAccess } from '@upbazaar/data-access';
import { provideDocumentTitle, provideI18n, provideInitialLanguage } from '@upbazaar/ui';
import { appRoutes } from './app.routes';
import { translations } from './i18n/translations';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes, withComponentInputBinding()),
    provideDataAccess({ interceptors: [authInterceptor] }),
    provideI18n(translations),
    provideInitialLanguage(),
    provideDocumentTitle('app.sellerPortal'),
  ],
};
