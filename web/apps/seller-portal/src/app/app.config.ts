import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideDataAccess } from '@upbazaar/data-access';
import { provideI18n, provideInitialLanguage } from '@upbazaar/ui';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes, withComponentInputBinding()),
    provideDataAccess(),
    provideI18n(),
    provideInitialLanguage(),
  ],
};
