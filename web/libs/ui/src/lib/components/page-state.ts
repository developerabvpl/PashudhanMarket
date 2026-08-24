import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * The three non-content states every list and detail screen needs: loading, empty, failed.
 *
 * Having one component means the skeleton, the empty copy and the retry affordance look and
 * behave the same in all three apps.
 */
@Component({
  selector: 'upb-page-state',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (state()) { @case ('loading') {
    <div class="space-y-3 p-6" [attr.aria-label]="'state.loading' | transloco" aria-busy="true" role="status">
      @for (row of skeletonRows; track row) {
      <div class="h-4 animate-pulse rounded-control bg-surface-sunken"></div>
      }
    </div>
    } @case ('error') {
    <div class="upb-card p-6 text-center" role="alert">
      <p class="font-medium text-ink">{{ message() ?? 'state.error' | transloco }}</p>
      <button
        type="button"
        class="mt-4 rounded-control bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
        (click)="retry.emit()"
      >
        {{ 'state.retry' | transloco }}
      </button>
    </div>
    } @case ('empty') {
    <div class="upb-card p-10 text-center">
      <p class="text-ink-muted">{{ message() ?? 'state.empty' | transloco }}</p>
    </div>
    } }
  `,
})
export class PageState {
  readonly state = input.required<'loading' | 'error' | 'empty'>();

  /** i18n key overriding the default copy for this state. */
  readonly message = input<string | null>(null);

  readonly retry = output<void>();

  protected readonly skeletonRows = [1, 2, 3, 4];
}
