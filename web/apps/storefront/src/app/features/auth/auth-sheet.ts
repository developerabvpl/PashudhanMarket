import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthPanel } from './auth-panel';

/** The bottom-sheet shell around {@link AuthPanel}. */
@Component({
  selector: 'upb-auth-sheet',
  imports: [AuthPanel, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="w-full rounded-t-card bg-surface p-5 shadow-raised sm:rounded-card">
      <div class="mx-auto mb-4 h-1 w-10 rounded-full bg-border sm:hidden" aria-hidden="true"></div>

      <button
        type="button"
        class="float-right -mt-1 rounded-control p-1 text-ink-muted hover:text-ink"
        [attr.aria-label]="'auth.closeSignIn' | transloco"
        (click)="dismiss()"
      >
        <span aria-hidden="true">&times;</span>
      </button>

      <upb-auth-panel (signedIn)="complete()" />
    </div>
  `,
})
export class AuthSheet {
  private readonly reference = inject<DialogRef<boolean>>(DialogRef);

  complete(): void {
    this.reference.close(true);
  }

  dismiss(): void {
    this.reference.close(false);
  }
}
