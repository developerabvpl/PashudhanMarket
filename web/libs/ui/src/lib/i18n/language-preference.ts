import {
  DOCUMENT,
  EnvironmentProviders,
  PLATFORM_ID,
  REQUEST,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { AVAILABLE_LANGUAGES, AppLanguage } from './provide-i18n';

export const LANGUAGE_COOKIE = 'upb.lang';

/** A year: the choice is a preference, not a session detail. */
const COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;

export function parseLanguageCookie(cookieHeader: string | null | undefined): AppLanguage | null {
  if (!cookieHeader) {
    return null;
  }

  for (const part of cookieHeader.split(';')) {
    const [name, ...rest] = part.trim().split('=');

    if (name === LANGUAGE_COOKIE) {
      const value = decodeURIComponent(rest.join('='));

      return (AVAILABLE_LANGUAGES as readonly string[]).includes(value)
        ? (value as AppLanguage)
        : null;
    }
  }

  return null;
}

export function writeLanguageCookie(document: Document, lang: AppLanguage): void {
  document.cookie = `${LANGUAGE_COOKIE}=${lang}; path=/; max-age=${COOKIE_MAX_AGE_SECONDS}; samesite=lax`;
}

/**
 * Applies the visitor's language before the first render.
 *
 * On the server the choice comes off the request cookie, which is what lets the storefront
 * emit Hindi HTML for a Hindi visitor. Without this the server would always render English
 * and a crawler would never see the translated page, however well the client switches after
 * hydration.
 */
export function provideInitialLanguage(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(async () => {
      const transloco = inject(TranslocoService);
      const platformId = inject(PLATFORM_ID);
      const document = inject(DOCUMENT);
      const request = inject(REQUEST, { optional: true });

      const cookieHeader = isPlatformBrowser(platformId)
        ? document.cookie
        : request?.headers.get('cookie');

      const lang = parseLanguageCookie(cookieHeader) ?? transloco.getDefaultLang();

      transloco.setActiveLang(lang);

      // Waiting here means the very first render already has the strings; otherwise the
      // server emits empty text nodes and hydration has to patch them in.
      await firstValueFrom(transloco.load(lang));
    }),
  ]);
}
