import { EnvironmentProviders, Injectable, isDevMode, makeEnvironmentProviders } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import en from './en.json';

export const AVAILABLE_LANGUAGES = ['en', 'hi'] as const;

export type AppLanguage = (typeof AVAILABLE_LANGUAGES)[number];

/**
 * Serves translations from the app's own bundle rather than fetching them over HTTP.
 *
 * English, the default and the fallback, is in the main bundle: the server render has its strings
 * already, where an HTTP loader would need an absolute URL during SSR or emit untranslated markup
 * that flickers on hydration. Hindi is its own chunk, loaded the first time someone picks it -
 * every string in every app, in a second language, is too much to make every visitor download.
 */
@Injectable({ providedIn: 'root' })
export class BundledTranslocoLoader implements TranslocoLoader {
  getTranslation(lang: string): Promise<Translation> {
    return lang === 'hi' ? import('./hi.json').then((m) => m.default as Translation) : Promise.resolve(en);
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
