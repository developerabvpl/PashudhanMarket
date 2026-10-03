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

  // The digits are asked for outright: hi-IN writes Western ones today, and this keeps it so
  // whatever a browser's own preference for Hindi is.
  const formatter = new Intl.DateTimeFormat(locale, { ...FORMATS[format], timeZone: TIME_ZONE, numberingSystem: 'latn' });

  return locale.startsWith('hi') ? withHindiDayPeriod(formatter, date) : formatter.format(date);
}

/** Before and after noon, as Hindi writes them out. */
const HINDI_DAY_PERIOD = { am: 'पूर्वाह्न', pm: 'अपराह्न' } as const;

/**
 * A Hindi date with its am/pm marker in Hindi too.
 *
 * The platform's own Hindi data leaves the marker in Latin letters - Intl gives
 * "23 अक्तू॰ 2026, 12:08 am" for hi-IN - which reads as a stray English word at the end of a Hindi
 * date. So the marker alone is swapped, part by part, for पूर्वाह्न or अपराह्न; everything else is
 * exactly what Intl wrote. A marker that is neither "am" nor "pm" - a browser that already writes
 * it in Hindi - is left as it came.
 */
function withHindiDayPeriod(formatter: Intl.DateTimeFormat, date: Date): string {
  return formatter
    .formatToParts(date)
    .map((part) => (part.type === 'dayPeriod' ? hindiDayPeriod(part.value) : part.value))
    .join('');
}

function hindiDayPeriod(marker: string): string {
  // "am", "AM" and "a.m." are all the same marker.
  const key = marker.toLowerCase().replace(/[^a-z]/g, '');

  return key === 'am' || key === 'pm' ? HINDI_DAY_PERIOD[key] : marker;
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
