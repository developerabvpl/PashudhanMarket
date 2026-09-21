import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ProductDto } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { productFacts } from '../../core/product-facts';
import { breadcrumbJsonLd, productJsonLd } from '../../core/product-jsonld';
import { SeoService } from '../../core/seo.service';
import { CartStore } from '../cart/cart.store';
import { ProductThumb, productPhotoUrl } from './product-thumb';

const ORIGIN = 'https://upbazaar.example';

/** Public product page: server-rendered, canonicalised, and marked up with JSON-LD. */
@Component({
  selector: 'upb-product-detail',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, ProductThumb],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (product(); as item) {
    <article class="mx-auto max-w-6xl px-4 py-6 sm:py-10">
      <nav aria-label="Breadcrumb" class="flex flex-wrap items-center gap-2 text-sm">
        <a class="text-ink-muted transition-colors hover:text-ink" routerLink="/products">
          {{ 'catalog.title' | transloco }}
        </a>
        <span class="text-ink-muted" aria-hidden="true">/</span>
        <a
          class="text-ink-muted transition-colors hover:text-ink"
          [routerLink]="['/products']"
          [queryParams]="{ category: item.category.id }"
        >
          {{ item.category.name }}
        </a>
      </nav>

      <div class="mt-6 grid gap-8 lg:grid-cols-[minmax(0,5fr)_minmax(0,6fr)] lg:gap-12">
        <div>
          <upb-product-thumb
            class="aspect-square w-full"
            size="detail"
            [name]="item.name"
            [sku]="item.sku"
          />
          @if (!photoUrl()) {
          <p class="mt-3 text-center text-xs text-ink-muted">
            {{ 'catalog.noPhotoYet' | transloco }}
          </p>
          }
        </div>

        <div>
          <p class="text-sm font-medium uppercase tracking-wider text-brand-700">
            {{ item.category.name }}
          </p>

          <h1 class="mt-2 text-2xl font-bold leading-tight tracking-tight text-ink sm:text-3xl">
            {{ item.name }}
          </h1>

          @if (item.brand) {
          <p class="mt-2 text-sm text-ink-muted">
            {{ 'catalog.byBrand' | transloco: { brand: item.brand } }}
          </p>
          }

          <p class="mt-1 text-sm text-ink-muted">
            {{ 'catalog.sku' | transloco }}: {{ item.sku }}
          </p>

          @if (facts().size || facts().pack || facts().dimension) {
          <ul class="mt-5 flex flex-wrap gap-2" [attr.aria-label]="'catalog.atAGlance' | transloco">
            @if (facts().size; as size) {
            <li class="rounded-full bg-surface-sunken px-3 py-1.5 text-sm font-medium text-ink">
              {{ size }}
            </li>
            } @if (facts().pack; as pack) {
            <li class="rounded-full bg-surface-sunken px-3 py-1.5 text-sm font-medium text-ink">
              {{ pack }}
            </li>
            } @if (facts().dimension; as dimension) {
            <li class="rounded-full bg-surface-sunken px-3 py-1.5 text-sm font-medium text-ink">
              {{ dimension }}
            </li>
            }
          </ul>
          }

          <div class="mt-6 rounded-card border border-border bg-surface p-5">
            @if (item.price > 0) {
            <p class="text-3xl font-bold text-ink">{{ item.price | inr: 'symbol' : 'auto' }}</p>
            <p
              class="mt-1 font-medium"
              [class.text-success]="available() > 0"
              [class.text-ink-muted]="available() === 0"
            >
              {{ (available() > 0 ? 'catalog.inStock' : 'catalog.outOfStock') | transloco }}
            </p>

            <button
              type="button"
              class="mt-5 w-full rounded-control bg-brand-600 px-6 py-3 font-semibold text-white transition-colors hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
              [disabled]="available() === 0"
              (click)="addToCart(item)"
            >
              {{ 'catalog.addToCart' | transloco }}
            </button>

            @if (inCart() > 0) {
            <p class="mt-3 text-center text-sm text-ink-muted">
              {{ 'cart.alreadyInCart' | transloco: { count: inCart() } }}
              <a class="text-accent-600 hover:underline" routerLink="/cart">
                {{ 'cart.viewCart' | transloco }}
              </a>
            </p>
            }
            } @else {
            <p class="text-2xl font-bold text-ink">{{ 'catalog.priceOnRequest' | transloco }}</p>
            <p class="mt-1 text-sm leading-relaxed text-ink-muted">
              {{ 'catalog.notYetPricedBody' | transloco }}
            </p>

            <button
              type="button"
              class="mt-5 w-full cursor-not-allowed rounded-control bg-brand-600 px-6 py-3 font-semibold text-white opacity-50"
              disabled
            >
              {{ 'catalog.addToCart' | transloco }}
            </button>
            }
          </div>

          @if (item.description) {
          <p class="mt-6 leading-relaxed text-ink">{{ item.description }}</p>
          }

          <dl class="mt-8 divide-y divide-border border-t border-border text-sm">
            @if (item.brand) {
            <div class="flex justify-between gap-4 py-3">
              <dt class="text-ink-muted">{{ 'catalog.brand' | transloco }}</dt>
              <dd class="text-right font-medium text-ink">{{ item.brand }}</dd>
            </div>
            }
            <div class="flex justify-between gap-4 py-3">
              <dt class="text-ink-muted">{{ 'catalog.category' | transloco }}</dt>
              <dd class="text-right font-medium text-ink">{{ item.category.name }}</dd>
            </div>
            <div class="flex justify-between gap-4 py-3">
              <dt class="text-ink-muted">{{ 'catalog.sku' | transloco }}</dt>
              <dd class="text-right font-medium text-ink">{{ item.sku }}</dd>
            </div>
            <div class="flex justify-between gap-4 py-3">
              <dt class="text-ink-muted">{{ 'catalog.currency' | transloco }}</dt>
              <dd class="text-right font-medium text-ink">{{ item.currency }}</dd>
            </div>
          </dl>
        </div>
      </div>
    </article>
    }
  `,
})
export class ProductDetail {
  private readonly seo = inject(SeoService);
  private readonly transloco = inject(TranslocoService);
  private readonly cart = inject(CartStore);
  private readonly toast = inject(ToastService);

  /** Resolved by productDetailResolver; null only while a failed load redirects away. */
  readonly product = input.required<ProductDto | null>();

  readonly available = computed(() => {
    const item = this.product();

    return item === null ? 0 : item.onHandQuantity - item.reservedQuantity;
  });

  /** How many of this product are already in the basket. */
  readonly inCart = computed(() => {
    const item = this.product();

    return item === null ? 0 : this.cart.quantityOf(item.id);
  });

  /** The category photograph, when the category has one. */
  readonly photoUrl = computed(() => {
    const item = this.product();

    return item === null ? null : productPhotoUrl(item.sku, item.name);
  });

  /** Pack size and weight, read out of the listing title. See core/product-facts. */
  readonly facts = computed(() => {
    const item = this.product();

    return productFacts(item?.name ?? '');
  });

  protected async addToCart(item: ProductDto): Promise<void> {
    if (await this.cart.add(item)) {
      this.toast.success('cart.added');
    }
  }

  constructor() {
    effect(() => {
      const item = this.product();

      if (item === null) {
        return;
      }

      const canonicalPath = `/products/${item.id}`;
      const canonicalUrl = `${ORIGIN}${canonicalPath}`;

      this.seo.apply({
        title: item.name,
        description: this.transloco.translate('catalog.productMetaDescription', {
          name: item.name,
          price: `${item.currency} ${item.price}`,
          availability: this.transloco.translate(
            this.available() > 0 ? 'catalog.availabilityInStock' : 'catalog.availabilityOutOfStock'
          ),
        }),
        canonicalPath,
        type: 'product',
      });

      this.seo.setJsonLd({
        '@context': 'https://schema.org',
        '@graph': [
          productJsonLd(item, canonicalUrl, this.photoUrl() && `${ORIGIN}${this.photoUrl()}`),
          breadcrumbJsonLd(item, ORIGIN, canonicalUrl),
        ],
      });
    });
  }
}
