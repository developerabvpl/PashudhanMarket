import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, PLATFORM_ID, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, PublicReviewDto, RatingSummaryDto, apiV1ReviewsProductsProductIdGet } from '@upbazaar/data-access';
import { StarRating } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';

const PAGE_SIZE = 10;

/**
 * A product's rating and reviews, under its details.
 *
 * Loaded in the browser rather than while prerendering: the page's HTML is built ahead of time,
 * and reviews baked into it would be as old as the last build. The rating counts every buyer who
 * rated; words and photos appear once staff have approved them, so some reviews are stars alone.
 */
@Component({
  selector: 'upb-product-reviews',
  imports: [TranslocoPipe, DateIstPipe, StarRating],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mt-12 border-t border-border pt-8" aria-labelledby="reviews-heading">
      <h2 id="reviews-heading" class="text-xl font-bold tracking-tight text-ink">{{ 'reviews.title' | transloco }}</h2>

      @if (summary(); as s) {
      @if (s.count === 0) {
      <p class="mt-3 text-sm text-ink-muted">{{ 'reviews.none' | transloco }}</p>
      } @else {
      <div class="mt-4 grid gap-6 sm:grid-cols-[14rem_1fr]">
        <div>
          <p class="text-4xl font-bold text-ink">{{ s.average }}</p>
          <upb-star-rating [rating]="s.average" size="1.25rem" />
          <p class="mt-1 text-sm text-ink-muted">
            {{ (s.count === 1 ? 'reviews.countOne' : 'reviews.countMany') | transloco: { count: s.count } }}
          </p>
        </div>

        <ul class="space-y-1 text-sm" [attr.aria-label]="'reviews.breakdown' | transloco">
          @for (row of breakdown(); track row.stars) {
          <li class="flex items-center gap-3">
            <span class="w-14 shrink-0 text-ink-muted">{{ 'reviews.starsRow' | transloco: { stars: row.stars } }}</span>
            <span class="h-2 flex-1 overflow-hidden rounded-full bg-surface-sunken" aria-hidden="true">
              <span class="block h-full rounded-full bg-accent-500" [style.width.%]="row.percent"></span>
            </span>
            <span class="w-8 shrink-0 text-right text-ink-muted">{{ row.count }}</span>
          </li>
          }
        </ul>
      </div>

      <ul class="mt-8 divide-y divide-border">
        @for (review of reviews(); track review.id) {
        <li class="py-5">
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
            <upb-star-rating [rating]="review.rating" />
            @if (review.title) {
            <p class="font-semibold text-ink">{{ review.title }}</p>
            }
          </div>
          <p class="mt-1 text-xs text-ink-muted">
            {{ 'reviews.byOn' | transloco: { name: review.reviewerName, date: (review.createdAtUtc | dateIst) } }}
          </p>
          @if (review.body) {
          <p class="mt-2 whitespace-pre-line text-sm text-ink">{{ review.body }}</p>
          }
          @if (review.photos.length > 0) {
          <div class="mt-3 flex flex-wrap gap-2">
            @for (photo of review.photos; track photo.id) {
            <a [href]="photo.url" target="_blank" rel="noopener">
              <img [src]="photo.url" [alt]="'reviews.photoAlt' | transloco: { name: review.reviewerName }" loading="lazy"
                class="size-20 rounded-control border border-border object-cover" />
            </a>
            }
          </div>
          }
          @if (review.reply; as reply) {
          <div class="mt-3 rounded-control bg-surface-sunken px-3 py-2 text-sm">
            <p class="font-medium text-ink">{{ 'reviews.sellerReply' | transloco }}</p>
            <p class="mt-1 whitespace-pre-line text-ink">{{ reply.text }}</p>
          </div>
          }
        </li>
        }
      </ul>

      @if (reviews().length < total()) {
      <button type="button" [disabled]="loading()"
        class="mt-2 rounded-control border border-border px-4 py-2 text-sm font-medium text-ink transition-colors hover:bg-surface-sunken disabled:opacity-50"
        (click)="more()">
        {{ 'reviews.more' | transloco }}
      </button>
      }
      }
      } @else if (failed()) {
      <p class="mt-3 text-sm text-ink-muted">{{ 'reviews.unavailable' | transloco }}</p>
      }
    </section>
  `,
})
export class ProductReviews {
  readonly productId = input.required<string>();

  /**
   * The rating as loaded, with the product it belongs to, so the page can put it in its
   * structured data without ever pairing one product with another's rating.
   */
  readonly rated = output<{ productId: string; summary: RatingSummaryDto }>();

  protected readonly summary = signal<RatingSummaryDto | null>(null);
  protected readonly reviews = signal<readonly PublicReviewDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);

  private page = 1;

  /** One row per star count, five stars first, as a share of all ratings. */
  protected readonly breakdown = computed(() => {
    const s = this.summary();

    if (!s || s.count === 0) {
      return [];
    }

    return [5, 4, 3, 2, 1].map((stars) => {
      const count = s.stars[stars - 1] ?? 0;

      return { stars, count, percent: Math.round((count / s.count) * 100) };
    });
  });

  private readonly api = inject(Api);

  constructor() {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) {
      return;
    }

    effect(() => {
      const productId = this.productId();

      untracked(() => void this.load(productId, 1));
    });
  }

  protected more(): Promise<void> {
    return this.load(this.productId(), this.page + 1);
  }

  private async load(productId: string, page: number): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    try {
      const result = await this.api.invoke(apiV1ReviewsProductsProductIdGet, { productId, page, pageSize: PAGE_SIZE });

      this.page = page;
      this.summary.set(result.summary);

      if (page === 1) {
        this.rated.emit({ productId, summary: result.summary });
      }

      this.total.set(result.totalCount);
      this.reviews.update((shown) => (page === 1 ? result.items : [...shown, ...result.items]));
    } catch {
      // Reviews are extra: the product page still works without them.
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
