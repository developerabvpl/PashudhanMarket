import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { ToastService, ToastTone } from './toast.service';

/**
 * Renders the toast queue. Mount once per app shell.
 *
 * The container is a polite live region so a screen reader announces a failure without
 * stealing focus from whatever the user was doing.
 */
@Component({
  selector: 'upb-toast-host',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-2 p-4 sm:items-end"
      role="region"
      [attr.aria-label]="'toast.region' | transloco"
    >
      @for (toast of toasts(); track toast.id) {
      <div
        class="upb-card pointer-events-auto flex w-full max-w-sm items-start gap-3 border-l-4 p-3"
        [class]="borderFor(toast.tone)"
        [attr.role]="toast.tone === 'error' ? 'alert' : 'status'"
        aria-live="polite"
      >
        <div class="min-w-0 flex-1">
          <p class="text-sm font-medium text-ink">{{ toast.message | transloco }}</p>
          @if (toast.detail) {
          <p class="mt-0.5 truncate font-mono text-xs text-ink-muted">{{ toast.detail }}</p>
          }
        </div>

        <button
          type="button"
          class="rounded-control p-1 text-ink-muted hover:text-ink"
          [attr.aria-label]="'toast.dismiss' | transloco"
          (click)="dismiss(toast.id)"
        >
          <span aria-hidden="true">&times;</span>
        </button>
      </div>
      }
    </div>
  `,
})
export class ToastHost {
  private readonly service = inject(ToastService);

  readonly toasts = this.service.toasts;

  dismiss(id: number): void {
    this.service.dismiss(id);
  }

  borderFor(tone: ToastTone): string {
    switch (tone) {
      case 'error':
        return 'border-l-danger';
      case 'success':
        return 'border-l-success';
      default:
        return 'border-l-info';
    }
  }
}
