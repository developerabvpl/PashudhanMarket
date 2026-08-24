import { Pipe, PipeTransform } from '@angular/core';

/**
 * Formats an amount in rupees using the Indian digit grouping (1,23,456.78) rather than the
 * thousands grouping Angular's CurrencyPipe would apply under a non-Indian locale.
 *
 * ```html
 * {{ product.price | inr }}                 <!-- ₹4,599.00 -->
 * {{ product.price | inr:'none' }}          <!-- 4,599.00  -->
 * {{ product.price | inr:'symbol':'auto' }} <!-- ₹4,599 -->
 * ```
 *
 * Money that is being reconciled — an order total, a payout — keeps its two decimals so the
 * columns line up and nothing looks rounded. A shelf price does not: 'auto' drops the paise on
 * a whole number and keeps them the moment there are any.
 */
@Pipe({ name: 'inr' })
export class InrCurrencyPipe implements PipeTransform {
  transform(
    value: number | string | null | undefined,
    symbol: 'symbol' | 'code' | 'none' = 'symbol',
    fractionDigits: number | 'auto' = 2
  ): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    const amount = typeof value === 'string' ? Number(value) : value;

    if (!Number.isFinite(amount)) {
      return '';
    }

    const digits = fractionDigits === 'auto' ? (Number.isInteger(amount) ? 0 : 2) : fractionDigits;

    const formatted = new Intl.NumberFormat('en-IN', {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }).format(amount);

    switch (symbol) {
      case 'symbol':
        return `₹${formatted}`;
      case 'code':
        return `INR ${formatted}`;
      default:
        return formatted;
    }
  }
}
