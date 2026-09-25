import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { InrCurrencyPipe } from '@upbazaar/util';

/**
 * A product's price as a shopper reads it: what it costs now, and while a sale runs, the regular
 * price struck through beside it with how much is off. The struck price is named for screen
 * readers, which would otherwise read two prices with nothing to tell them apart.
 */
@Component({
  selector: 'upb-sale-price',
  imports: [TranslocoPipe, InrCurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="flex flex-wrap items-baseline gap-x-2">
      <span class="font-bold text-ink" [class]="large() ? 'text-3xl' : 'text-lg'">
        {{ current() | inr: 'symbol' : 'auto' }}
      </span>
      @if (percentOff(); as off) {
      <span class="text-ink-muted line-through" [class]="large() ? 'text-lg' : 'text-sm'">
        <span class="upb-sr-only">{{ 'catalog.sale.was' | transloco }}</span>
        {{ regular() | inr: 'symbol' : 'auto' }}
      </span>
      <span class="font-semibold text-success" [class]="large() ? 'text-base' : 'text-xs'">
        {{ 'catalog.sale.percentOff' | transloco: { percent: off } }}
      </span>
      }
    </p>
  `,
})
export class SalePrice {
  /** The regular price. */
  readonly regular = input.required<number>();

  /** What it costs now; below the regular price while a sale runs. */
  readonly current = input.required<number>();

  /** The product page's headline size rather than a card's. */
  readonly large = input(false);

  /** Whole percent off, rounded down so it never claims more than it gives; null with no sale. */
  protected readonly percentOff = computed(() => {
    const regular = this.regular();
    const current = this.current();

    return current < regular && regular > 0 ? Math.max(1, Math.floor(((regular - current) / regular) * 100)) : null;
  });
}
