import {
  DOCUMENT,
  DestroyRef,
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';

/**
 * Keeps `<html lang>` on the language showing.
 *
 * index.html says "en", and nothing changed it when the visitor picked Hindi: a screen reader then
 * read Devanagari with English pronunciation rules, the browser offered to translate a page that
 * was already translated, and a crawler filed the Hindi storefront under English.
 *
 * Part of {@link provideI18n}, so no app can forget it. It follows Transloco's active language
 * rather than the cookie, which makes it right in all three places the language is set: the
 * server render (provideInitialLanguage sets the language from the request cookie before the
 * first render, so the HTML leaves the server with the right attribute), the browser's start-up,
 * and the language switcher.
 */
export function provideDocumentLanguage(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideEnvironmentInitializer(() => {
      const document = inject(DOCUMENT);

      inject(TranslocoService)
        .langChanges$.pipe(takeUntilDestroyed(inject(DestroyRef)))
        .subscribe((lang) => document.documentElement.setAttribute('lang', lang));
    }),
  ]);
}
