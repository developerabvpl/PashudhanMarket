import { ChangeDetectionStrategy, Component, DOCUMENT, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AVAILABLE_LANGUAGES, AppLanguage } from '../i18n/provide-i18n';
import { writeLanguageCookie } from '../i18n/language-preference';

/**
 * Switches between English and Hindi. A plain select rather than a custom menu: it is
 * keyboard-operable and screen-reader-labelled for free.
 *
 * Each option carries its own `selected` rather than the select a `value`. Angular sets the
 * select's properties before the @for block has rendered any options, so a `[value]="'hi'"`
 * lands on an empty select, is dropped, and the browser then selects the first option to
 * arrive - English - while the page is in Hindi. The binding never re-fires because its value
 * never changed.
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
        (change)="switch($event)"
      >
        @for (lang of languages; track lang) {
        <option [value]="lang" [selected]="lang === active()">
          {{ (lang === 'en' ? 'app.english' : 'app.hindi') | transloco }}
        </option>
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

  /**
   * Follows Transloco rather than remembering the last pick, so a language set anywhere else -
   * the cookie at start-up, a sign-in that restores a preference - shows here too.
   */
  protected readonly active = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  switch(event: Event): void {
    const lang = (event.target as HTMLSelectElement).value as AppLanguage;

    writeLanguageCookie(this.document, lang);
    this.transloco.setActiveLang(lang);

    if (this.reloadOnSwitch()) {
      this.document.defaultView?.location.reload();
    }
  }
}
