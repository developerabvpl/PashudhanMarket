import { Pipe, PipeTransform } from '@angular/core';

/**
 * Formats an amount in rupees using the Indian digit grouping (1,23,456.78) rather than the
 * thousands grouping Angular's CurrencyPipe would apply under a non-Indian locale.
 *
 * ```html
 * {{ product.price | inr }}          <!-- ₹4,599.00 -->
 * {{ product.price | inr:'none' }}   <!-- 4,599.00  -->
 * ```
 */
@Pipe({ name: 'inr' })
export class InrCurrencyPipe implements PipeTransform {
  transform(
    value: number | string | null | undefined,
    symbol: 'symbol' | 'code' | 'none' = 'symbol',
    fractionDigits = 2
  ): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    const amount = typeof value === 'string' ? Number(value) : value;

    if (!Number.isFinite(amount)) {
      return '';
    }

    const formatted = new Intl.NumberFormat('en-IN', {
      minimumFractionDigits: fractionDigits,
      maximumFractionDigits: fractionDigits,
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
