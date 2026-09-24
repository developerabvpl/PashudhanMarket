import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, ReviewDto, apiV1SellerReviewsGet } from '@upbazaar/data-access';
import { StarRating } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';
import { ReviewReply } from './review-reply';

/**
 * What buyers say about the seller's products, newest first, each with the seller's reply.
 *
 * The stars show at once; the buyer's words and photos only once staff approve them, which is
 * what the storefront shows too. A seller who wants to answer every review can narrow the list
 * to the ones still without a reply.
 */
@Component({
  selector: 'upb-seller-reviews-page',
  imports: [TranslocoPipe, DateIstPipe, StarRating, MatCheckboxModule, MatPaginatorModule, MatProgressBarModule, ReviewReply],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'reviews.sellerTitle' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'reviews.sellerSubtitle' | transloco }}</p>

      <mat-checkbox class="mt-4" [checked]="unanswered()" (change)="filter($event.checked)">
        {{ 'reviews.unanswered' | transloco }}
      </mat-checkbox>

      <div class="upb-card mt-4">
        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
        <ul class="divide-y divide-border">
          @for (review of reviews(); track review.id) {
          <li class="space-y-2 p-4">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <p class="font-medium text-ink">{{ review.productName }}</p>
              <p class="text-sm text-ink-muted">{{ review.reviewerName }} · {{ review.createdAtUtc | dateIst }}</p>
            </div>
            <div class="flex flex-wrap items-center gap-3">
              <upb-star-rating [rating]="review.rating" />
              @if (review.title) { <p class="font-semibold text-ink">{{ review.title }}</p> }
            </div>
            @if (review.body) {
            <p class="whitespace-pre-line text-sm text-ink">{{ review.body }}</p>
            } @else if (review.contentStatus === 'Pending') {
            <p class="text-sm text-ink-muted">{{ 'reviews.waitingForApproval' | transloco }}</p>
            } @else if (review.contentStatus === 'Rejected') {
            <p class="text-sm text-ink-muted">{{ 'reviews.notPublished' | transloco }}</p>
            }
            @if (review.photos.length > 0) {
            <div class="flex flex-wrap gap-2">
              @for (photo of review.photos; track photo.id) {
              <a [href]="photo.url" target="_blank" rel="noopener">
                <img [src]="photo.url" [alt]="'reviews.photoAlt' | transloco: { name: review.reviewerName }"
                  class="size-16 rounded-control border border-border object-cover" loading="lazy" />
              </a>
              }
            </div>
            }
            <upb-review-reply [review]="review" (replied)="replace($event)" />
          </li>
          } @empty {
          @if (!loading()) { <li class="p-8 text-center text-ink-muted">{{ 'reviews.noneForSeller' | transloco }}</li> }
          }
        </ul>
        <mat-paginator [length]="total()" [pageSize]="pageSize" [pageIndex]="page() - 1" [hidePageSize]="true" (page)="turn($event)" />
      </div>
    </section>
  `,
})
export class ReviewsPage {
  protected readonly pageSize = 25;
  protected readonly reviews = signal<readonly ReviewDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly unanswered = signal(false);
  protected readonly loading = signal(false);

  private readonly api = inject(Api);

  constructor() {
    void this.load();
  }

  protected filter(unanswered: boolean): void {
    this.unanswered.set(unanswered);
    this.page.set(1);
    void this.load();
  }

  protected turn(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.load();
  }

  /** Shows the reply just posted without reloading; it stays in an "unanswered" list until the next load. */
  protected replace(updated: ReviewDto): void {
    this.reviews.update((list) => list.map((r) => (r.id === updated.id ? updated : r)));
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1SellerReviewsGet, {
        Page: this.page(),
        PageSize: this.pageSize,
        Unanswered: this.unanswered(),
      });

      this.reviews.set(result.items);
      this.total.set(result.totalCount);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
