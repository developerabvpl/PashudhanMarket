import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Five stars, filled to a rating, for showing a review or a product's average.
 *
 * The stars themselves are hidden from screen readers, which would otherwise read "star" five
 * times; the label says the number instead. An average is rounded to the nearest whole star
 * for the picture, and the label keeps the decimal.
 *
 * ```html
 * <upb-star-rating [rating]="4.3" />
 * ```
 */
@Component({
  selector: 'upb-star-rating',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="inline-flex items-center leading-none" role="img" [attr.aria-label]="'reviews.starsLabel' | transloco: { rating: rating() }">
      @for (filled of stars(); track $index) {
      <span aria-hidden="true" [class]="filled ? 'text-accent-500' : 'text-border'" [style.font-size]="size()">★</span>
      }
    </span>
  `,
})
export class StarRating {
  readonly rating = input.required<number>();

  /** Any CSS length; defaults to the surrounding text size. */
  readonly size = input<string>('1em');

  protected readonly stars = computed(() => {
    const whole = Math.round(this.rating());

    return [1, 2, 3, 4, 5].map((n) => n <= whole);
  });
}
