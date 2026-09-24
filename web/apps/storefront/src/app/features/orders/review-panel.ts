import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  MyReviewDto,
  ReviewDto,
  apiV1ReviewsMineProductIdGet,
  apiV1ReviewsMineProductIdPhotosPhotoIdDelete,
  apiV1ReviewsMineProductIdPhotosPost,
  apiV1ReviewsMineProductIdPut,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, StarRating, ToastService } from '@upbazaar/ui';

/** Largest photo the API accepts; checked here too so a buyer is not left waiting on a doomed upload. */
export const MAX_PHOTO_BYTES = 5 * 1024 * 1024;

/**
 * A delivered product's review, under its line on the order page: the button to rate it, the form
 * to write or change the review, and its photos.
 *
 * The stars count as soon as they are saved; words and photos wait for staff, which the panel says
 * plainly so an unpublished review is not a surprise. Photos can be added once the review exists,
 * because they hang on it.
 */
@Component({
  selector: 'upb-review-panel',
  imports: [TranslocoPipe, FieldErrors, StarRating],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state(); as s) {
    @if (s.canReview || s.review) {
    <div class="mt-2 text-sm">
      @if (!open()) {
      @if (s.review; as review) {
      <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span class="text-ink-muted">{{ 'reviews.yours' | transloco }}</span>
        <upb-star-rating [rating]="review.rating" />
        <button type="button" class="font-medium text-accent-600 hover:underline" (click)="edit()">
          {{ 'reviews.edit' | transloco }}
        </button>
      </div>
      <p class="mt-1 text-ink-muted" role="status">
        {{ 'reviews.status.' + review.contentStatus | transloco: { note: review.moderationNote ?? '' } }}
      </p>
      } @else {
      <button type="button" class="font-medium text-accent-600 hover:underline" (click)="edit()">
        {{ 'reviews.rate' | transloco }}
      </button>
      }
      } @else {
      <form class="mt-2 space-y-3 rounded-control border border-border p-3" novalidate (submit)="save($event)">
        <fieldset>
          <legend class="font-medium text-ink">{{ 'reviews.ratingLabel' | transloco }}</legend>
          <div class="mt-1 flex gap-1" role="radiogroup" [attr.aria-describedby]="id('rating') + '-errors'">
            @for (n of [1, 2, 3, 4, 5]; track n) {
            <button type="button" role="radio" class="text-2xl leading-none transition-colors"
              [class]="n <= rating() ? 'text-accent-500' : 'text-border hover:text-accent-400'"
              [attr.aria-checked]="rating() === n" [attr.aria-label]="'reviews.starsLabel' | transloco: { rating: n }"
              (click)="rating.set(n)">★</button>
            }
          </div>
          <upb-field-errors [fieldId]="id('rating')" [errors]="errors('rating')" />
        </fieldset>

        <div>
          <label class="block font-medium text-ink" [for]="id('title')">
            {{ 'reviews.titleLabel' | transloco }} <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span>
          </label>
          <input [id]="id('title')" maxlength="120" [class]="inputClass" [value]="title()" (input)="title.set(value($event))"
            [attr.aria-invalid]="errors('title').length > 0" [attr.aria-describedby]="id('title') + '-errors'" />
          <upb-field-errors [fieldId]="id('title')" [errors]="errors('title')" />
        </div>

        <div>
          <label class="block font-medium text-ink" [for]="id('body')">
            {{ 'reviews.bodyLabel' | transloco }} <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span>
          </label>
          <textarea [id]="id('body')" rows="4" maxlength="2000" [class]="inputClass" [value]="body()" (input)="body.set(value($event))"
            [attr.aria-invalid]="errors('body').length > 0" [attr.aria-describedby]="id('body') + '-hint ' + id('body') + '-errors'"></textarea>
          <p [id]="id('body') + '-hint'" class="mt-1 text-xs text-ink-muted">{{ 'reviews.bodyHint' | transloco }}</p>
          <upb-field-errors [fieldId]="id('body')" [errors]="errors('body')" />
        </div>

        <upb-field-errors [fieldId]="id('form')" [errors]="generalErrors()" />

        <div class="flex flex-wrap items-center gap-3">
          <button type="submit" [disabled]="busy()"
            class="rounded-control bg-brand-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50">
            {{ 'reviews.save' | transloco }}
          </button>
          <button type="button" class="text-ink-muted hover:underline" (click)="open.set(false)">
            {{ 'reviews.close' | transloco }}
          </button>
        </div>

        @if (s.review; as review) {
        @if (s.photosEnabled) {
        <div class="border-t border-border pt-3">
          <p class="font-medium text-ink">{{ 'reviews.photos' | transloco }}</p>
          <p [id]="id('photo') + '-hint'" class="text-xs text-ink-muted">{{ 'reviews.photosHint' | transloco: { max: s.maxPhotos } }}</p>

          <ul class="mt-2 flex flex-wrap gap-2">
            @for (photo of review.photos; track photo.id; let i = $index) {
            <li class="relative">
              <img [src]="photo.url" [alt]="'reviews.yourPhoto' | transloco: { n: i + 1 }" class="size-20 rounded-control border border-border object-cover" />
              <button type="button" [disabled]="busy()"
                class="absolute -right-2 -top-2 rounded-full bg-surface px-1.5 text-xs font-bold text-danger shadow ring-1 ring-border disabled:opacity-50"
                [attr.aria-label]="'reviews.removePhoto' | transloco: { n: i + 1 }" (click)="removePhoto(photo.id)">✕</button>
            </li>
            }
          </ul>

          @if (review.photos.length < s.maxPhotos) {
          <!-- The input is hidden but still focusable; its label shows the focus ring in its place. -->
          <input [id]="id('photo')" type="file" accept="image/jpeg,image/png,image/webp" class="peer sr-only" [disabled]="busy()"
            [attr.aria-describedby]="id('photo') + '-hint ' + id('photo') + '-errors'" (change)="addPhoto($event)" />
          <label class="mt-2 inline-block cursor-pointer rounded-control font-medium text-accent-600 hover:underline peer-focus-visible:ring-2 peer-focus-visible:ring-accent-500"
            [for]="id('photo')">
            {{ 'reviews.addPhoto' | transloco }}
          </label>
          }
          <upb-field-errors [fieldId]="id('photo')" [errors]="photoErrors()" />
        </div>
        }
        }
      </form>
      }
    </div>
    }
    }
  `,
})
export class ReviewPanel {
  readonly productId = input.required<string>();

  protected readonly inputClass =
    'mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink aria-[invalid=true]:border-danger';

  protected readonly state = signal<MyReviewDto | null>(null);
  protected readonly open = signal(false);
  protected readonly rating = signal(0);
  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly busy = signal(false);
  protected readonly photoErrors = signal<readonly string[]>([]);
  private readonly problem = signal<ApiProblem | null>(null);
  private readonly ratingMissing = signal(false);

  /** Messages that name no field of the form, such as "this was changed at the same time". */
  protected readonly generalErrors = computed(() => {
    const problem = this.problem();

    return problem && Object.keys(problem.fieldErrors).length === 0 ? [problem.title] : [];
  });

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      const productId = this.productId();

      untracked(() => void this.load(productId));
    });
  }

  protected id(name: string): string {
    return `review-${name}-${this.productId()}`;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected errors(field: string): readonly string[] {
    if (field === 'rating' && this.ratingMissing()) {
      return ['reviews.chooseRating'];
    }

    return fieldErrorsFor(this.problem(), field);
  }

  /** Opens the form, filled with the saved review if there is one. */
  protected edit(): void {
    const review = this.state()?.review;

    this.rating.set(review?.rating ?? 0);
    this.title.set(review?.title ?? '');
    this.body.set(review?.body ?? '');
    this.problem.set(null);
    this.photoErrors.set([]);
    this.ratingMissing.set(false);
    this.open.set(true);
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.problem.set(null);
    this.ratingMissing.set(this.rating() < 1);

    if (this.ratingMissing()) {
      return;
    }

    await this.run(async () => {
      const review = await this.api.invoke(apiV1ReviewsMineProductIdPut, {
        productId: this.productId(),
        body: { rating: this.rating(), title: this.title().trim() || null, body: this.body().trim() || null },
      });

      this.show(review);
      this.toast.success('reviews.saved');

      // Stay open when photos can be added, since they need the review to exist first.
      if (!this.state()?.photosEnabled) {
        this.open.set(false);
      }
    }, (problem) => this.problem.set(problem));
  }

  protected async addPhoto(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    this.photoErrors.set([]);

    if (!file) {
      return;
    }

    if (file.size > MAX_PHOTO_BYTES) {
      this.photoErrors.set(['reviews.photoTooLarge']);

      return;
    }

    await this.run(async () => {
      this.show(await this.api.invoke(apiV1ReviewsMineProductIdPhotosPost, { productId: this.productId(), body: { file } }));
      this.toast.success('reviews.photoAdded');
    }, (problem) => this.photoErrors.set([problem.title]));
  }

  protected async removePhoto(photoId: string): Promise<void> {
    this.photoErrors.set([]);

    await this.run(async () => {
      this.show(await this.api.invoke(apiV1ReviewsMineProductIdPhotosPhotoIdDelete, { productId: this.productId(), photoId }));
    }, (problem) => this.photoErrors.set([problem.title]));
  }

  private async load(productId: string): Promise<void> {
    try {
      this.state.set(await this.api.invoke(apiV1ReviewsMineProductIdGet, { productId }));
    } catch {
      // The order page is about the order; without its review panel it still does its job.
      this.state.set(null);
    }
  }

  private show(review: ReviewDto): void {
    this.state.update((s) => (s ? { ...s, review } : s));
  }

  private async run(work: () => Promise<void>, fail: (problem: ApiProblem) => void): Promise<void> {
    this.busy.set(true);

    try {
      await work();
    } catch (error) {
      fail(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
