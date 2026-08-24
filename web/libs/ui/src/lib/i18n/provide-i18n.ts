import { EnvironmentProviders, Injectable, isDevMode, makeEnvironmentProviders } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import en from './en.json';
import hi from './hi.json';

export const AVAILABLE_LANGUAGES = ['en', 'hi'] as const;

export type AppLanguage = (typeof AVAILABLE_LANGUAGES)[number];

const TRANSLATIONS: Record<AppLanguage, Translation> = { en, hi };

/**
 * Serves translations from the bundle rather than fetching them.
 *
 * The two files are a few kilobytes, and bundling means the server render has the strings
 * already: an HTTP loader would either need an absolute URL during SSR or emit untranslated
 * markup that flickers on hydration.
 */
@Injectable({ providedIn: 'root' })
export class BundledTranslocoLoader implements TranslocoLoader {
  getTranslation(lang: string): Promise<Translation> {
    return Promise.resolve(TRANSLATIONS[lang as AppLanguage] ?? TRANSLATIONS.en);
  }
}

export function provideI18n(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        availableLangs: [...AVAILABLE_LANGUAGES],
        defaultLang: 'en',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        missingHandler: {
          // A missing key is a bug; shout about it in dev, fall back quietly in production.
          logMissingKey: isDevMode(),
          useFallbackTranslation: true,
        },
        prodMode: !isDevMode(),
      },
      loader: BundledTranslocoLoader,
    }),
  ]);
}
