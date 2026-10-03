import { ChangeDetectorRef, OnDestroy, Pipe, PipeTransform, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';

export type IstDateFormat = 'date' | 'datetime' | 'time' | 'short';

const TIME_ZONE = 'Asia/Kolkata';

const FORMATS: Record<IstDateFormat, Intl.DateTimeFormatOptions> = {
  date: { day: '2-digit', month: 'short', year: 'numeric' },
  datetime: {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: true,
  },
  time: { hour: '2-digit', minute: '2-digit', hour12: true },
  short: { day: '2-digit', month: '2-digit', year: '2-digit' },
};

/**
 * The locale dates are written in for an app language: Hindi month names in Hindi, and Indian
 * English for English or anything else. Both keep Western digits, as the prices beside them do.
 */
export function istDateLocale(lang: string | null | undefined): string {
  return lang === 'hi' ? 'hi-IN' : 'en-IN';
}

/**
 * A UTC timestamp from the API, written out in Asia/Kolkata in the given locale; nothing for a
 * value that is missing or is not a date.
 */
export function formatIst(
  value: string | number | Date | null | undefined,
  format: IstDateFormat = 'date',
  locale = 'en-IN'
): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }

  const date = value instanceof Date ? value : new Date(value);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  return new Intl.DateTimeFormat(locale, { ...FORMATS[format], timeZone: TIME_ZONE }).format(date);
}

/**
 * Renders a UTC timestamp from the API in Asia/Kolkata, in the language showing.
 *
 * Fixing the zone rather than using the browser's matters twice over: an order placed at
 * 23:30 IST must not read as the previous day for a seller travelling abroad, and the server
 * render must agree with the client render or hydration flags a mismatch.
 *
 * The month is named in the app's language - "03 अक्तू॰ 2026" in Hindi, where it used to stay
 * "03 Oct 2026" in the middle of a Hindi sentence. It follows Transloco's active language, which
 * the server render sets from the visitor's cookie before drawing, so server and browser agree.
 * It uses the platform's own Intl data rather than Angular's DatePipe, so no locale data has to be
 * registered (registerLocaleData) or shipped in a bundle.
 *
 * Impure because its output depends on the language as well as its input: a pure pipe would keep
 * the English date after a switch to Hindi. The last result is kept, so a change-detection pass
 * that changes nothing costs a comparison, not a format.
 */
@Pipe({ name: 'dateIst', pure: false })
export class DateIstPipe implements PipeTransform, OnDestroy {
  /** Absent only where no translations are provided at all; dates then read in Indian English. */
  private readonly transloco = inject(TranslocoService, { optional: true });
  private readonly changes = inject(ChangeDetectorRef, { optional: true });

  /** An OnPush view is not checked again just because the language changed; ask for it. */
  private readonly languageChanges: Subscription | undefined = this.transloco?.langChanges$.subscribe(() =>
    this.changes?.markForCheck()
  );

  private last: { value: unknown; format: IstDateFormat; locale: string; text: string } | null = null;

  transform(value: string | number | Date | null | undefined, format: IstDateFormat = 'date'): string {
    const locale = istDateLocale(this.transloco?.getActiveLang());
    const last = this.last;

    if (last && last.value === value && last.format === format && last.locale === locale) {
      return last.text;
    }

    const text = formatIst(value, format, locale);
    this.last = { value, format, locale, text };

    return text;
  }

  ngOnDestroy(): void {
    this.languageChanges?.unsubscribe();
  }
}
