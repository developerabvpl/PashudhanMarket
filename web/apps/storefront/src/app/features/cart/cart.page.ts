import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { SeoService } from '../../core/seo.service';
import { ProductThumb } from '../products/product-thumb';
import { CartStore, MAX_QUANTITY } from './cart.store';

/** The basket. Client-side only until the Cart and Ordering modules exist. */
@Component({
  selector: 'upb-cart-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, ProductThumb],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8 sm:py-12">
      <h1 class="text-2xl font-bold tracking-tight text-ink sm:text-3xl">
        {{ 'cart.title' | transloco }}
      </h1>

      @if (cart.isEmpty()) {
      <div class="mt-10 rounded-card border border-border bg-surface p-10 text-center">
        <p class="text-ink-muted">{{ 'cart.empty' | transloco }}</p>
        <a
          class="mt-5 inline-block rounded-control bg-brand-600 px-5 py-2.5 font-semibold text-white transition-colors hover:bg-brand-700"
          routerLink="/products"
        >
          {{ 'cart.browse' | transloco }}
        </a>
      </div>
      } @else {
      <p class="mt-2 text-sm text-ink-muted" aria-live="polite">
        {{ 'cart.itemCount' | transloco: { count: cart.count() } }}
      </p>

      <ul class="mt-6 divide-y divide-border rounded-card border border-border bg-surface">
        @for (line of cart.lines(); track line.productId) {
        <li class="flex gap-4 p-4">
          <a class="w-20 shrink-0 sm:w-24" [routerLink]="['/products', line.productId]">
            <upb-product-thumb class="aspect-square w-full" [name]="line.name" [sku]="line.sku" />
          </a>

          <div class="flex min-w-0 flex-1 flex-col gap-2">
            <div class="flex flex-wrap items-start justify-between gap-x-4 gap-y-1">
              <a
                class="min-w-0 text-sm font-medium leading-snug text-ink hover:text-brand-700"
                [routerLink]="['/products', line.productId]"
              >
                {{ line.name }}
              </a>
              <p class="shrink-0 font-bold text-ink">
                {{ line.price * line.quantity | inr: 'symbol' : 'auto' }}
              </p>
            </div>

            <p class="text-xs text-ink-muted">
              {{ line.sku }} · {{ line.price | inr: 'symbol' : 'auto' }}
              {{ 'cart.each' | transloco }}
            </p>

            <div class="mt-auto flex items-center gap-3">
              <div
                class="flex items-center rounded-control border border-border"
                role="group"
                [attr.aria-label]="'cart.quantityFor' | transloco: { name: line.name }"
              >
                <button
                  type="button"
                  class="px-3 py-1.5 text-lg leading-none text-ink transition-colors hover:bg-surface-sunken disabled:opacity-40"
                  [disabled]="line.quantity <= 1"
                  [attr.aria-label]="'cart.decrease' | transloco"
                  (click)="cart.setQuantity(line.productId, line.quantity - 1)"
                >
                  −
                </button>
                <span class="min-w-10 text-center text-sm font-medium text-ink">
                  {{ line.quantity }}
                </span>
                <button
                  type="button"
                  class="px-3 py-1.5 text-lg leading-none text-ink transition-colors hover:bg-surface-sunken disabled:opacity-40"
                  [disabled]="line.quantity >= maxQuantity"
                  [attr.aria-label]="'cart.increase' | transloco"
                  (click)="cart.setQuantity(line.productId, line.quantity + 1)"
                >
                  +
                </button>
              </div>

              <button
                type="button"
                class="text-sm text-ink-muted underline-offset-2 transition-colors hover:text-danger hover:underline"
                (click)="removeLine(line.productId)"
              >
                {{ 'cart.remove' | transloco }}
              </button>
            </div>
          </div>
        </li>
        }
      </ul>

      <div class="mt-6 rounded-card border border-border bg-surface p-5">
        <div class="flex items-baseline justify-between">
          <p class="font-medium text-ink">{{ 'cart.subtotal' | transloco }}</p>
          <p class="text-2xl font-bold text-ink">
            {{ cart.subtotal() | inr: 'symbol' : 'auto' }}
          </p>
        </div>
        <p class="mt-1 text-sm text-ink-muted">{{ 'cart.subtotalNote' | transloco }}</p>

        <button
          type="button"
          class="mt-5 w-full cursor-not-allowed rounded-control bg-brand-600 px-6 py-3 font-semibold text-white opacity-50"
          disabled
        >
          {{ 'cart.checkout' | transloco }}
        </button>
        <p class="mt-2 text-center text-sm text-ink-muted">
          {{ 'cart.checkoutUnavailable' | transloco }}
        </p>
      </div>

      <div class="mt-6 flex flex-wrap items-center justify-between gap-3">
        <a class="text-sm text-accent-600 hover:underline" routerLink="/products">
          {{ 'cart.keepShopping' | transloco }}
        </a>
        <button
          type="button"
          class="text-sm text-ink-muted underline-offset-2 transition-colors hover:text-danger hover:underline"
          (click)="clear()"
        >
          {{ 'cart.clear' | transloco }}
        </button>
      </div>
      }
    </section>
  `,
})
export class CartPage {
  protected readonly cart = inject(CartStore);
  protected readonly maxQuantity = MAX_QUANTITY;

  private readonly seo = inject(SeoService);
  private readonly transloco = inject(TranslocoService);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      this.seo.apply({
        title: this.transloco.translate('cart.title'),
        description: this.transloco.translate('cart.metaDescription'),
        canonicalPath: '/cart',
        type: 'website',
        // A basket is per-visitor and has nothing to offer a crawler.
        noIndex: true,
      });

      this.seo.setJsonLd(null);
    });
  }

  protected removeLine(productId: string): void {
    this.cart.remove(productId);
    this.toast.info('cart.removed');
  }

  protected clear(): void {
    this.cart.clear();
    this.toast.info('cart.cleared');
  }
}
