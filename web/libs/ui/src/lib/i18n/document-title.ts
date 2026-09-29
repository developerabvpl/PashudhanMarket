import {
  DestroyRef,
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';

/**
 * Keeps the browser tab's title on one translated string, in whichever language is showing.
 *
 * For the portals, whose screens all carry the same name: without it the tab shows index.html's
 * static title, which does not change with the language. The storefront names each page itself
 * for search engines, so it does not use this.
 */
export function provideDocumentTitle(key: string): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideEnvironmentInitializer(() => {
      const title = inject(Title);

      inject(TranslocoService)
        .selectTranslate(key)
        .pipe(takeUntilDestroyed(inject(DestroyRef)))
        .subscribe((text) => title.setTitle(text));
    }),
  ]);
}
