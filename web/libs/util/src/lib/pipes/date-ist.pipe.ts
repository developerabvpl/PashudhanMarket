import { Pipe, PipeTransform } from '@angular/core';

export type IstDateFormat = 'date' | 'datetime' | 'time' | 'short';

/**
 * Renders a UTC timestamp from the API in Asia/Kolkata.
 *
 * Fixing the zone rather than using the browser's matters twice over: an order placed at
 * 23:30 IST must not read as the previous day for a seller travelling abroad, and the server
 * render must agree with the client render or hydration flags a mismatch.
 */
@Pipe({ name: 'dateIst' })
export class DateIstPipe implements PipeTransform {
  private static readonly TimeZone = 'Asia/Kolkata';

  private static readonly Formats: Record<IstDateFormat, Intl.DateTimeFormatOptions> = {
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

  transform(
    value: string | number | Date | null | undefined,
    format: IstDateFormat = 'date'
  ): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    const date = value instanceof Date ? value : new Date(value);

    if (Number.isNaN(date.getTime())) {
      return '';
    }

    return new Intl.DateTimeFormat('en-IN', {
      ...DateIstPipe.Formats[format],
      timeZone: DateIstPipe.TimeZone,
    }).format(date);
  }
}
