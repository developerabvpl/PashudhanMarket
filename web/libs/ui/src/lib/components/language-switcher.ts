import { ChangeDetectionStrategy, Component, DOCUMENT, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AVAILABLE_LANGUAGES, AppLanguage } from '../i18n/provide-i18n';
import { writeLanguageCookie } from '../i18n/language-preference';

/**
 * Switches between English and Hindi. A plain select rather than a custom menu: it is
 * keyboard-operable and screen-reader-labelled for free.
 */
@Component({
  selector: 'upb-language-switcher',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="flex items-center gap-2 text-sm">
      <span class="upb-sr-only">{{ 'app.languageLabel' | transloco }}</span>
      <select
        class="rounded-control border border-border bg-surface px-2 py-1 text-ink"
        [value]="active()"
        (change)="switch($event)"
      >
        @for (lang of languages; track lang) {
        <option [value]="lang">{{ (lang === 'en' ? 'app.english' : 'app.hindi') | transloco }}</option>
        }
      </select>
    </label>
  `,
})
export class LanguageSwitcher {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  /**
   * Reload after switching, so a server-rendered app re-renders in the new language.
   *
   * The storefront sets this: its pages are rendered on the server for search engines, and a
   * client-side swap would leave the indexed HTML in the old language. The two SPA portals
   * leave it off and switch in place.
   */
  readonly reloadOnSwitch = input(false);

  protected readonly languages = AVAILABLE_LANGUAGES;

  protected readonly active = signal<AppLanguage>(
    (this.transloco.getActiveLang() as AppLanguage) ?? 'en'
  );

  switch(event: Event): void {
    const lang = (event.target as HTMLSelectElement).value as AppLanguage;

    this.active.set(lang);
    writeLanguageCookie(this.document, lang);
    this.transloco.setActiveLang(lang);

    if (this.reloadOnSwitch()) {
      this.document.defaultView?.location.reload();
    }
  }
}
