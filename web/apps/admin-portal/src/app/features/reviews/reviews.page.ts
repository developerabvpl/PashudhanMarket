import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  ReviewDto,
  apiV1AdminReviewsGet,
  apiV1AdminReviewsReviewIdApprovePost,
  apiV1AdminReviewsReviewIdRejectPost,
  apiV1AdminReviewsReviewIdReplyHidePost,
} from '@upbazaar/data-access';
import { StarRating, ToastService } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';
import { ReviewsPermissions } from '../../core/permissions';

/** The filters, the queue first. "all" sends no status. */
export const REVIEW_FILTERS = ['Pending', 'Approved', 'Rejected', 'None', 'all'] as const;

/**
 * Buyers' reviews, opening on the words and photos waiting for approval, oldest first: each is a
 * buyer waiting to see their review go up. A moderator approves them, or rejects them with a note
 * the buyer reads - so the note should say what to change. The stars count either way.
 *
 * Sellers' replies are shown here too, with a way to hide one that crosses a line. Staff who may
 * only read reviews see the same page without the buttons.
 */
@Component({
  selector: 'upb-reviews-page',
  imports: [
    TranslocoPipe,
    DateIstPipe,
    StarRating,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'reviews.adminTitle' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'reviews.adminSubtitle' | transloco }}</p>

      <mat-button-toggle-group class="mt-4" [attr.aria-label]="'reviews.filterLabel' | transloco" [value]="filter()" (change)="show($event.value)">
        @for (f of filters; track f) {
        <mat-button-toggle [value]="f">{{ 'reviews.filter.' + f | transloco }}</mat-button-toggle>
        }
      </mat-button-toggle-group>

      <div class="mt-4 grid gap-4 lg:grid-cols-[22rem_1fr]">
        <div class="upb-card h-fit">
          @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
          <ul class="divide-y divide-border">
            @for (review of reviews(); track review.id) {
            <li>
              <button type="button" class="w-full p-3 text-left hover:bg-surface-sunken"
                [class.bg-surface-sunken]="selected()?.id === review.id" (click)="selectedId.set(review.id)">
                <p class="font-medium text-ink">{{ review.productName }}</p>
                <p class="flex items-center gap-2 text-sm text-ink-muted">
                  <upb-star-rating [rating]="review.rating" /> {{ review.reviewerName }}
                </p>
                <p class="text-xs text-ink-muted">{{ (review.contentSubmittedAtUtc ?? review.createdAtUtc) | dateIst: 'datetime' }}</p>
              </button>
            </li>
            } @empty {
            @if (!loading()) { <li class="p-6 text-center text-ink-muted">{{ 'reviews.nothingHere' | transloco }}</li> }
            }
          </ul>
          <mat-paginator [length]="total()" [pageSize]="pageSize" [pageIndex]="page() - 1" [hidePageSize]="true" (page)="turn($event)" />
        </div>

        @if (selected(); as r) {
        <article class="upb-card space-y-3 p-5 text-sm">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <h2 class="text-lg font-semibold text-ink">{{ r.productName }}</h2>
            <span class="text-ink-muted">{{ 'reviews.filter.' + r.contentStatus | transloco }}</span>
          </div>
          <p class="text-ink-muted">
            {{ 'reviews.reviewer' | transloco }}: {{ r.reviewerName }}
            @if (r.contentSubmittedAtUtc) { · {{ 'reviews.submitted' | transloco: { date: (r.contentSubmittedAtUtc | dateIst: 'datetime') } }} }
          </p>

          <div class="flex flex-wrap items-center gap-3">
            <upb-star-rating [rating]="r.rating" size="1.25rem" />
            @if (r.title) { <p class="font-semibold text-ink">{{ r.title }}</p> }
          </div>
          @if (r.body) {
          <p class="whitespace-pre-line text-ink">{{ r.body }}</p>
          }
          @if (r.photos.length > 0) {
          <div class="flex flex-wrap gap-2">
            @for (photo of r.photos; track photo.id) {
            <a [href]="photo.url" target="_blank" rel="noopener">
              <img [src]="photo.url" [alt]="'reviews.photoAlt' | transloco: { name: r.reviewerName }"
                class="size-32 rounded-control border border-border object-cover" />
            </a>
            }
          </div>
          }
          @if (r.contentStatus === 'None') {
          <p class="text-ink-muted">{{ 'reviews.starsOnly' | transloco }}</p>
          }
          @if (r.contentStatus === 'Rejected' && r.moderationNote) {
          <p class="text-danger">{{ 'reviews.rejectedBecause' | transloco: { note: r.moderationNote } }}</p>
          }

          @if (canModerate() && r.contentStatus === 'Pending') {
          <div class="space-y-3 border-t border-border pt-4">
            <button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="approve(r)">
              {{ 'reviews.approve' | transloco }}
            </button>
            <mat-form-field class="w-full" subscriptSizing="dynamic">
              <mat-label>{{ 'reviews.rejectNote' | transloco }}</mat-label>
              <textarea matInput rows="2" maxlength="500" [value]="note()" (input)="note.set(value($event))"></textarea>
            </mat-form-field>
            <button mat-stroked-button type="button" [disabled]="busy() || !note().trim()" (click)="reject(r)">
              {{ 'reviews.reject' | transloco }}
            </button>
          </div>
          }

          @if (r.reply; as reply) {
          <div class="rounded-control bg-surface-sunken p-3">
            <p class="font-medium text-ink">{{ 'reviews.sellerReply' | transloco }}</p>
            <p class="mt-1 whitespace-pre-line text-ink">{{ reply.text }}</p>
            @if (reply.hidden) {
            <p class="mt-1 text-danger">{{ 'reviews.replyHidden' | transloco }}</p>
            } @else if (canModerate()) {
            <button mat-stroked-button color="warn" type="button" class="mt-2" [disabled]="busy()" (click)="hideReply(r)">
              {{ 'reviews.hideReply' | transloco }}
            </button>
            }
          </div>
          }
        </article>
        }
      </div>
    </section>
  `,
})
export class ReviewsPage {
  protected readonly filters = REVIEW_FILTERS;
  protected readonly pageSize = 25;
  protected readonly reviews = signal<readonly ReviewDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly filter = signal<string>('Pending');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);

  protected readonly selected = computed(() => this.reviews().find((r) => r.id === this.selectedId()) ?? null);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);
  private readonly currentUser = inject(CurrentUserStore);

  protected readonly canModerate = computed(() => this.currentUser.has(ReviewsPermissions.Moderate));

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected show(filter: string): void {
    this.filter.set(filter);
    this.page.set(1);
    this.selectedId.set(null);
    void this.load();
  }

  protected turn(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.load();
  }

  protected approve(review: ReviewDto): Promise<void> {
    return this.act(async () => {
      await this.api.invoke(apiV1AdminReviewsReviewIdApprovePost, { reviewId: review.id });
      this.toast.success('reviews.approved');
    });
  }

  protected reject(review: ReviewDto): Promise<void> {
    return this.act(async () => {
      await this.api.invoke(apiV1AdminReviewsReviewIdRejectPost, { reviewId: review.id, body: { note: this.note().trim() } });
      this.toast.info('reviews.rejected');
    });
  }

  protected hideReply(review: ReviewDto): Promise<void> {
    return this.act(async () => {
      await this.api.invoke(apiV1AdminReviewsReviewIdReplyHidePost, { reviewId: review.id });
      this.toast.info('reviews.replyHiddenToast');
    });
  }

  /** Runs a decision, then reloads: a decided review leaves the queue, and the next one is shown. */
  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);

    try {
      await work();
      this.note.set('');
      await this.load();
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const filter = this.filter();
      const result = await this.api.invoke(apiV1AdminReviewsGet, {
        Status: filter === 'all' ? undefined : filter,
        Page: this.page(),
        PageSize: this.pageSize,
      });

      this.reviews.set(result.items);
      this.total.set(result.totalCount);

      // Keep the open review open if it is still listed; otherwise move on to the first.
      if (!result.items.some((r) => r.id === this.selectedId())) {
        this.selectedId.set(result.items[0]?.id ?? null);
      }
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
