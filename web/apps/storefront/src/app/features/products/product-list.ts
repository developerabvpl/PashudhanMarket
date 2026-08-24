import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { CategoryDto, PagedListOfProductSummaryDto } from '@upbazaar/data-access';
import { PageState } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { SeoService } from '../../core/seo.service';
import { ProductThumb } from './product-thumb';

/**
 * Public catalogue. Data arrives from the route resolvers as inputs, so the first paint is
 * server-rendered with real products and real filter links.
 */
@Component({
  selector: 'upb-product-list',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, PageState, ProductThumb],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="border-b border-border bg-surface">
      <div class="mx-auto max-w-7xl px-4 py-10 sm:py-14">
        <p class="text-sm font-medium uppercase tracking-widest text-brand-700">
          {{ 'catalog.heroEyebrow' | transloco }}
        </p>
        <h1 class="mt-3 max-w-2xl text-3xl font-bold tracking-tight text-ink sm:text-4xl">
          {{ 'catalog.heroTitle' | transloco }}
        </h1>
        <p class="mt-3 max-w-2xl text-base leading-relaxed text-ink-muted">
          {{ 'catalog.heroSubtitle' | transloco }}
        </p>

        <form class="mt-7 flex max-w-2xl gap-2" role="search" (submit)="search($event)">
          <label class="flex-1">
            <span class="upb-sr-only">{{ 'catalog.searchLabel' | transloco }}</span>
            <input
              name="q"
              type="search"
              class="w-full rounded-control border border-border bg-surface px-4 py-3 text-ink shadow-card placeholder:text-ink-muted"
              [attr.value]="queryText()"
              [attr.placeholder]="'catalog.searchPlaceholder' | transloco"
            />
          </label>
          <button
            type="submit"
            class="shrink-0 rounded-control bg-brand-600 px-5 py-3 font-semibold text-white transition-colors hover:bg-brand-700"
          >
            {{ 'catalog.searchLabel' | transloco }}
          </button>
        </form>
      </div>
    </section>

    <section class="mx-auto max-w-7xl px-4 py-8">
      @if (categories().length > 0) {
      <nav class="-mx-1 overflow-x-auto pb-1" [attr.aria-label]="'catalog.categories' | transloco">
        <ul class="flex w-max gap-2 px-1 md:w-auto md:flex-wrap">
          <li>
            <a
              class="block whitespace-nowrap rounded-full border px-4 py-2 text-sm font-medium transition-colors"
              [class]="chipClass(null)"
              [routerLink]="['/products']"
              [queryParams]="{ category: null, page: null }"
              queryParamsHandling="merge"
              [attr.aria-current]="activeCategoryId() === null ? 'true' : null"
            >
              {{ 'catalog.allCategories' | transloco }}
            </a>
          </li>
          @for (category of categories(); track category.id) {
          <li>
            <a
              class="block whitespace-nowrap rounded-full border px-4 py-2 text-sm font-medium transition-colors"
              [class]="chipClass(category.id)"
              [routerLink]="['/products']"
              [queryParams]="{ category: category.id, page: null }"
              queryParamsHandling="merge"
              [attr.aria-current]="activeCategoryId() === category.id ? 'true' : null"
            >
              {{ category.name }}
            </a>
          </li>
          }
        </ul>
      </nav>
      }

      <p class="mt-6 text-sm text-ink-muted" aria-live="polite">
        {{ 'catalog.resultCount' | transloco: { count: page().totalCount } }}
      </p>

      @if (items().length === 0) {
      <div class="py-12">
        <upb-page-state state="empty" />
      </div>
      } @else {
      <ul class="mt-4 grid grid-cols-2 gap-4 sm:gap-5 lg:grid-cols-3 xl:grid-cols-4">
        @for (product of items(); track product.id) {
        <li>
          <a
            class="upb-card group flex h-full flex-col overflow-hidden transition-shadow hover:shadow-raised focus-visible:outline-none focus-visible:shadow-raised"
            [routerLink]="['/products', product.id]"
          >
            <upb-product-thumb
              class="aspect-square w-full"
              [name]="product.name"
              [sku]="product.sku"
            />

            <div class="flex flex-1 flex-col p-4">
              <h2
                class="line-clamp-3 text-sm font-medium leading-snug text-ink group-hover:text-brand-700"
              >
                {{ product.name }}
              </h2>

              <p class="mt-1 text-xs text-ink-muted">{{ product.sku }}</p>

              <div class="mt-auto pt-3">
                @if (product.price > 0) {
                <p class="text-lg font-bold text-ink">{{ product.price | inr: 'symbol' : 'auto' }}</p>
                <p
                  class="mt-0.5 text-xs font-medium"
                  [class.text-success]="product.availableQuantity > 0"
                  [class.text-ink-muted]="product.availableQuantity === 0"
                >
                  {{
                    (product.availableQuantity > 0 ? 'catalog.inStock' : 'catalog.outOfStock')
                      | transloco
                  }}
                </p>
                } @else {
                <p class="text-sm font-semibold text-ink">
                  {{ 'catalog.priceOnRequest' | transloco }}
                </p>
                <p class="mt-0.5 text-xs text-ink-muted">
                  {{ 'catalog.notYetPriced' | transloco }}
                </p>
                }
              </div>
            </div>
          </a>
        </li>
        }
      </ul>

      @if (totalPages() > 1) {
      <nav
        class="mt-10 flex items-center justify-between gap-4"
        [attr.aria-label]="'catalog.pagination' | transloco"
      >
        @if (currentPage() > 1) {
        <a
          class="rounded-control border border-border px-4 py-2 text-sm font-medium text-ink transition-colors hover:bg-surface-sunken"
          [routerLink]="['/products']"
          [queryParams]="{ page: currentPage() - 1 }"
          queryParamsHandling="merge"
          rel="prev"
        >
          {{ 'catalog.previousPage' | transloco }}
        </a>
        } @else {
        <span
          class="rounded-control border border-border px-4 py-2 text-sm font-medium text-ink-muted opacity-50"
        >
          {{ 'catalog.previousPage' | transloco }}
        </span>
        }

        <p class="text-sm text-ink-muted">
          {{
            'catalog.pageOf'
              | transloco: { page: currentPage(), totalPages: totalPages() }
          }}
        </p>

        @if (hasNextPage()) {
        <a
          class="rounded-control border border-border px-4 py-2 text-sm font-medium text-ink transition-colors hover:bg-surface-sunken"
          [routerLink]="['/products']"
          [queryParams]="{ page: currentPage() + 1 }"
          queryParamsHandling="merge"
          rel="next"
        >
          {{ 'catalog.nextPage' | transloco }}
        </a>
        } @else {
        <span
          class="rounded-control border border-border px-4 py-2 text-sm font-medium text-ink-muted opacity-50"
        >
          {{ 'catalog.nextPage' | transloco }}
        </span>
        }
      </nav>
      } }
    </section>
  `,
})
export class ProductList {
  private readonly seo = inject(SeoService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  /** Resolved by productListResolver and bound through withComponentInputBinding(). */
  readonly page = input.required<PagedListOfProductSummaryDto>();

  /** Resolved by categoriesResolver; empty when the taxonomy call failed. */
  readonly categories = input<CategoryDto[]>([]);

  /**
   * Current search term, echoed back into the input after a reload.
   *
   * Bound as an attribute, not a property. This page is server-rendered, so the search box is
   * on screen and typeable before hydration finishes; a `[value]` property binding re-applies
   * itself during hydration and wipes whatever was typed in that window. Setting the attribute
   * seeds the initial value and then leaves a dirty input alone, which is what a browser's own
   * back-navigation restore does too.
   */
  readonly q = input<string>('');

  /** Current category filter, read from the query string to highlight the active chip. */
  readonly category = input<string>('');

  readonly items = computed(() => this.page().items ?? []);

  readonly queryText = computed(() => this.q() ?? '');

  readonly activeCategoryId = computed(() => this.category() || null);

  // The contract makes these optional; a catalogue with one page reports neither.
  readonly currentPage = computed(() => this.page().page);

  readonly totalPages = computed(() => this.page().totalPages ?? 1);

  readonly hasNextPage = computed(() => this.page().hasNextPage ?? false);

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

  /** Selected chips carry the brand fill; the rest stay quiet so the selection is obvious. */
  protected chipClass(categoryId: string | null): string {
    return this.activeCategoryId() === categoryId
      ? 'border-brand-600 bg-brand-600 text-white'
      : 'border-border bg-surface text-ink-muted hover:border-brand-300 hover:text-ink';
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
