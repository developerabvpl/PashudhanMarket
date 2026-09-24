import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, ReviewDto, apiV1SellerReviewsReviewIdReplyPut } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';

/**
 * The seller's one public reply to a review: written here, shown under the review at once, and
 * rewritten here too. When staff have hidden it the seller is told, since otherwise they would
 * keep editing a reply nobody can see.
 */
@Component({
  selector: 'upb-review-reply',
  imports: [TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (review().reply?.hidden) {
    <p class="mb-2 text-sm text-danger" role="status">{{ 'reviews.replyHidden' | transloco }}</p>
    }
    <div class="flex flex-wrap items-start gap-3">
      <mat-form-field class="min-w-64 flex-1" subscriptSizing="dynamic">
        <mat-label>{{ 'reviews.replyLabel' | transloco }}</mat-label>
        <textarea matInput rows="2" maxlength="1000" [value]="text()" (input)="text.set(value($event))"></textarea>
        <mat-hint>{{ 'reviews.replyHint' | transloco }}</mat-hint>
      </mat-form-field>
      <button mat-stroked-button type="button" [disabled]="busy() || !text().trim() || text().trim() === review().reply?.text" (click)="send()">
        {{ (review().reply ? 'reviews.updateReply' : 'reviews.postReply') | transloco }}
      </button>
    </div>
  `,
})
export class ReviewReply {
  readonly review = input.required<ReviewDto>();
  readonly replied = output<ReviewDto>();

  protected readonly text = signal('');
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    // Start from what is saved, so an edit changes the reply rather than retyping it.
    effect(() => this.text.set(this.review().reply?.text ?? ''));
  }

  protected value(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected async send(): Promise<void> {
    this.busy.set(true);

    try {
      const updated = await this.api.invoke(apiV1SellerReviewsReviewIdReplyPut, {
        reviewId: this.review().id,
        body: { text: this.text().trim() },
      });

      this.toast.success('reviews.replied');
      this.replied.emit(updated);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }
}
