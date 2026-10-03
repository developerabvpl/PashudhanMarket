import {
  ApplicationRef,
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
 * On `<html>` while a page drawn in one language waits for the app to redraw it in the visitor's.
 *
 * The storefront's catalogue pages are prerendered - in English, at build time, with no visitor
 * and so no cookie. A returning Hindi visitor used to watch that English page turn Hindi once the
 * app had started. A few lines in the storefront's index.html now run before anything is painted:
 * they put the visitor's language on `<html lang>` and, when the page in hand was drawn in another,
 * add this class, which keeps the app's root from showing (index.html holds the rule). The app
 * takes it off again once it has drawn the page in the right language - see
 * {@link provideInitialLanguage} - and the script takes it off itself after a few seconds, so a
 * script that fails to load leaves an English page rather than a blank one.
 */
export const LANGUAGE_PENDING_CLASS = 'upb-lang-pending';

/**
 * Applies the visitor's language before the first render.
 *
 * On the server the choice comes off the request cookie, which is what lets the storefront
 * emit Hindi HTML for a Hindi visitor. Without this the server would always render English
 * and a crawler would never see the translated page, however well the client switches after
 * hydration.
 *
 * In the browser it also shows the page again if index.html held it back
 * ({@link LANGUAGE_PENDING_CLASS}): once the app is stable - the first navigation done, its
 * resolvers answered, the page drawn in the visitor's language - or straight away if the strings
 * could not be loaded, since an English page is better than none.
 */
export function provideInitialLanguage(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(async () => {
      const transloco = inject(TranslocoService);
      const platformId = inject(PLATFORM_ID);
      const document = inject(DOCUMENT);
      const request = inject(REQUEST, { optional: true });
      const application = inject(ApplicationRef);
      const browser = isPlatformBrowser(platformId);

      const cookieHeader = browser ? document.cookie : request?.headers.get('cookie');

      const lang = parseLanguageCookie(cookieHeader) ?? transloco.getDefaultLang();
      const reveal = (): void => document.documentElement.classList.remove(LANGUAGE_PENDING_CLASS);

      transloco.setActiveLang(lang);

      try {
        // Waiting here means the very first render already has the strings; otherwise the
        // server emits empty text nodes and hydration has to patch them in.
        await firstValueFrom(transloco.load(lang));
      } catch (error) {
        reveal();

        throw error;
      }

      if (browser) {
        void application.whenStable().then(reveal, reveal);
      }
    }),
  ]);
}
