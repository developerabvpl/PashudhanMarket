import { EnvironmentProviders, Injectable, InjectionToken, inject, isDevMode, makeEnvironmentProviders } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { provideDocumentLanguage } from './document-language';

export const AVAILABLE_LANGUAGES = ['en', 'hi'] as const;

export type AppLanguage = (typeof AVAILABLE_LANGUAGES)[number];

/**
 * Where an app's strings come from, one loader per language.
 *
 * Each app passes its own copy, generated from this library's en.json and hi.json by
 * `npm run i18n:split`, which holds only the keys that app uses: the shared files carry every
 * screen of all three apps, and a buyer on the storefront has no use for the admin portal's.
 */
export type AppTranslations = Readonly<Record<AppLanguage, () => Promise<Translation>>>;

/**
 * Every string, both languages, each loaded on first use. For specs and anything else without an
 * app copy of its own; an app build never calls these, so the full files stay out of its bundles.
 */
export const ALL_TRANSLATIONS: AppTranslations = {
  en: () => import('./en.json').then((m) => m.default as Translation),
  hi: () => import('./hi.json').then((m) => m.default as Translation),
};

const APP_TRANSLATIONS = new InjectionToken<AppTranslations>('APP_TRANSLATIONS');

/**
 * Serves translations from the app's own bundle rather than fetching them over HTTP.
 *
 * An app hands in English already imported, so it sits in the main bundle: the server render has
 * its strings, where an HTTP loader would need an absolute URL during SSR or emit untranslated
 * markup that flickers on hydration. Hindi is its own chunk, loaded the first time someone picks
 * it. An unknown language gets English, the fallback.
 */
@Injectable({ providedIn: 'root' })
export class BundledTranslocoLoader implements TranslocoLoader {
  private readonly translations = inject(APP_TRANSLATIONS, { optional: true }) ?? ALL_TRANSLATIONS;

  getTranslation(lang: string): Promise<Translation> {
    return (lang === 'hi' ? this.translations.hi : this.translations.en)();
  }
}

export function provideI18n(translations: AppTranslations = ALL_TRANSLATIONS): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: APP_TRANSLATIONS, useValue: translations },
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
    // <html lang> follows the language showing, in every app that has translations at all.
    provideDocumentLanguage(),
  ]);
}
