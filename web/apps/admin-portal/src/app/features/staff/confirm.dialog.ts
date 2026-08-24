import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';

/** What a confirmation asks, and how loudly. */
export interface ConfirmData {
  /** i18n key for the heading. */
  readonly title: string;
  /** i18n key for the body. */
  readonly body: string;
  /** Interpolation values for the body. */
  readonly params?: Record<string, unknown>;
  /** i18n key for the confirming button. */
  readonly confirmLabel: string;
  /** Colours the action as destructive. */
  readonly destructive?: boolean;
}

/**
 * Confirmation for a change somebody will later have to answer for.
 *
 * Every one of these spells out the consequence in the body rather than asking "are you
 * sure?", and every one carries the note that the change is recorded against the caller's
 * account. An administrator changing another person's access should be reminded that the act
 * is attributable, at the moment they do it.
 */
@Component({
  selector: 'upb-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title | transloco }}</h2>

    <mat-dialog-content>
      <p>{{ data.body | transloco: data.params }}</p>
      <p class="mt-3 text-sm text-ink-muted">{{ 'staff.auditNote' | transloco }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="false">
        {{ 'common.cancel' | transloco }}
      </button>
      <button
        mat-flat-button
        type="button"
        [color]="data.destructive ? 'warn' : 'primary'"
        [mat-dialog-close]="true"
      >
        {{ data.confirmLabel | transloco }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);

  protected readonly reference = inject<MatDialogRef<ConfirmDialog, boolean>>(MatDialogRef);
}

/** Opens {@link ConfirmDialog} and resolves to the answer. */
export async function confirm(dialog: MatDialog, data: ConfirmData): Promise<boolean> {
  const reference = dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
    data,
    width: '32rem',
    autoFocus: 'dialog',
  });

  return (await firstValue(reference)) === true;
}

function firstValue(reference: MatDialogRef<ConfirmDialog, boolean>): Promise<boolean | undefined> {
  return new Promise((resolve) => {
    reference.afterClosed().subscribe((result) => resolve(result));
  });
}
