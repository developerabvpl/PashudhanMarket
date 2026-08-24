import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PagedListOfProductSummaryDto } from '@upbazaar/data-access';
import { PageState } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { SeoService } from '../../core/seo.service';

/**
 * Public catalogue. Data arrives from the route resolver as an input, so the first paint is
 * server-rendered with real products.
 */
@Component({
  selector: 'upb-product-list',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, PageState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'catalog.title' | transloco }}</h1>

      <form class="mt-4 flex gap-2" role="search" (submit)="search($event)">
        <label class="flex-1">
          <span class="upb-sr-only">{{ 'catalog.searchLabel' | transloco }}</span>
          <input
            name="q"
            type="search"
            class="w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [value]="queryText()"
            [attr.placeholder]="'catalog.searchPlaceholder' | transloco"
          />
        </label>
        <button
          type="submit"
          class="rounded-control bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700"
        >
          {{ 'catalog.searchLabel' | transloco }}
        </button>
      </form>

      <p class="mt-3 text-sm text-ink-muted" aria-live="polite">
        {{ 'catalog.resultCount' | transloco: { count: page().totalCount } }}
      </p>

      @if (items().length === 0) {
      <upb-page-state state="empty" />
      } @else {
      <ul class="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        @for (product of items(); track product.id) {
        <li class="upb-card p-4">
          <a
            class="block focus-visible:outline-none"
            [routerLink]="['/products', product.id]"
          >
            <h2 class="font-medium text-ink">{{ product.name }}</h2>
            <p class="mt-1 text-sm text-ink-muted">
              {{ 'catalog.sku' | transloco }}: {{ product.sku }}
            </p>
            <p class="mt-2 text-lg font-semibold text-ink">{{ product.price | inr }}</p>
            <p
              class="mt-1 text-sm"
              [class.text-success]="product.availableQuantity > 0"
              [class.text-ink-muted]="product.availableQuantity === 0"
            >
              {{
                (product.availableQuantity > 0 ? 'catalog.inStock' : 'catalog.outOfStock')
                  | transloco
              }}
            </p>
          </a>
        </li>
        }
      </ul>
      }
    </section>
  `,
})
export class ProductList {
  private readonly seo = inject(SeoService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  /** Resolved by productListResolver and bound through withComponentInputBinding(). */
  readonly page = input.required<PagedListOfProductSummaryDto>();

  /** Current search term, echoed back into the input after a reload. */
  readonly q = input<string>('');

  readonly items = computed(() => this.page().items ?? []);

  readonly queryText = computed(() => this.q() ?? '');

  constructor() {
    effect(() => {
      const term = this.queryText();

      this.seo.apply({
        title: term
          ? `${this.transloco.translate('catalog.searchLabel')}: ${term}`
          : this.transloco.translate('catalog.title'),
        description: this.transloco.translate('catalog.metaDescription'),
        // Search results are one canonical catalogue page, not a page per query string.
        canonicalPath: '/products',
        type: 'website',
      });

      this.seo.setJsonLd(null);
    });
  }

  search(event: Event): void {
    event.preventDefault();

    const form = event.target as HTMLFormElement;
    const term = new FormData(form).get('q')?.toString().trim() ?? '';

    void this.router.navigate(['/products'], {
      queryParams: { q: term === '' ? null : term, page: null },
      queryParamsHandling: 'merge',
    });
  }
}
