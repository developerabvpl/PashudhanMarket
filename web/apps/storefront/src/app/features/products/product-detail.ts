import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { NgOptimizedImage } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ProductDto } from '@upbazaar/data-access';
import { InrCurrencyPipe } from '@upbazaar/util';
import { breadcrumbJsonLd, productJsonLd } from '../../core/product-jsonld';
import { SeoService } from '../../core/seo.service';

const ORIGIN = 'https://upbazaar.example';

/** Public product page: server-rendered, canonicalised, and marked up with JSON-LD. */
@Component({
  selector: 'upb-product-detail',
  imports: [RouterLink, NgOptimizedImage, TranslocoPipe, InrCurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (product(); as item) {
    <article class="mx-auto max-w-4xl px-4 py-8">
      <nav aria-label="Breadcrumb" class="text-sm">
        <a class="text-accent-600 hover:underline" routerLink="/products">
          {{ 'catalog.backToProducts' | transloco }}
        </a>
      </nav>

      <div class="mt-6 grid gap-8 md:grid-cols-2">
        <img
          class="w-full rounded-card bg-surface-sunken object-cover"
          [ngSrc]="imageUrl()"
          [alt]="item.name"
          width="640"
          height="640"
          priority
        />

        <div>
          <h1 class="text-3xl font-semibold text-ink">{{ item.name }}</h1>
          <p class="mt-1 text-sm text-ink-muted">
            {{ 'catalog.sku' | transloco }}: {{ item.sku }}
          </p>

          <p class="mt-4 text-3xl font-bold text-ink">{{ item.price | inr }}</p>

          <p
            class="mt-2 font-medium"
            [class.text-success]="available() > 0"
            [class.text-ink-muted]="available() === 0"
          >
            {{ (available() > 0 ? 'catalog.inStock' : 'catalog.outOfStock') | transloco }}
          </p>

          @if (item.description) {
          <p class="mt-6 leading-relaxed text-ink">{{ item.description }}</p>
          }

          <button
            type="button"
            class="mt-8 w-full rounded-control bg-brand-600 px-6 py-3 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
            [disabled]="available() === 0"
          >
            {{ 'catalog.addToCart' | transloco }}
          </button>
        </div>
      </div>
    </article>
    }
  `,
})
export class ProductDetail {
  private readonly seo = inject(SeoService);
  private readonly transloco = inject(TranslocoService);

  /** Resolved by productDetailResolver; null only while a failed load redirects away. */
  readonly product = input.required<ProductDto | null>();

  readonly available = computed(() => {
    const item = this.product();

    return item === null ? 0 : item.onHandQuantity - item.reservedQuantity;
  });

  /**
   * The API has no image field yet, so this is a deterministic placeholder keyed on the SKU.
   * Swap for the real asset URL once catalog images land.
   */
  readonly imageUrl = computed(() => {
    const item = this.product();

    return item === null ? '' : `/assets/products/${item.sku.toLowerCase()}.jpg`;
  });

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
          productJsonLd(item, canonicalUrl),
          breadcrumbJsonLd(item, ORIGIN, canonicalUrl),
        ],
      });
    });
  }
}
