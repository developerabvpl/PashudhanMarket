import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { RoleDto, UserSummaryDto } from '@upbazaar/data-access';
import { roleLabel } from './role-label';

/** The user whose roles are being edited, and the roles available. */
export interface StaffRolesData {
  readonly user: UserSummaryDto;
  readonly roles: readonly RoleDto[];
}

/**
 * Picks the roles a user should hold.
 *
 * Shows what each role grants next to it, because "FinanceOfficer" tells an administrator
 * nothing about whether ticking it hands over the ability to approve payouts. The dialog only
 * collects the selection; the caller confirms and saves, so the confirmation can name exactly
 * what is about to change.
 */
@Component({
  selector: 'upb-staff-roles-dialog',
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'staff.rolesTitle' | transloco: { name: data.user.displayName } }}</h2>

    <mat-dialog-content>
      <ul class="space-y-3 pt-2">
        @for (role of data.roles; track role.name) {
        <li>
          <mat-checkbox [checked]="isSelected(role.name)" (change)="toggle(role.name)">
            <span class="font-medium">{{ roleName(role.name) }}</span>
          </mat-checkbox>
          @if (role.description) {
          <p class="ml-9 text-sm text-ink-muted">{{ role.description }}</p>
          } @if (role.permissions.length > 0) {
          <p class="ml-9 font-mono text-xs text-ink-muted">{{ role.permissions.join(', ') }}</p>
          }
        </li>
        }
      </ul>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="reference.close()">
        {{ 'common.cancel' | transloco }}
      </button>
      <button mat-flat-button color="primary" type="button" (click)="save()">
        {{ 'common.save' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
})
export class StaffRolesDialog {
  protected readonly data = inject<StaffRolesData>(MAT_DIALOG_DATA);

  protected readonly reference =
    inject<MatDialogRef<StaffRolesDialog, string[] | undefined>>(MatDialogRef);

  private readonly selected = signal<Set<string>>(new Set(this.data.user.roles));

  private readonly transloco = inject(TranslocoService);

  /** The role as people read it; its API name is still what is saved. */
  protected roleName(name: string): string {
    return roleLabel(this.transloco, name);
  }

  protected isSelected(role: string): boolean {
    return this.selected().has(role);
  }

  protected toggle(role: string): void {
    this.selected.update((current) => {
      const next = new Set(current);

      if (!next.delete(role)) {
        next.add(role);
      }

      return next;
    });
  }

  protected save(): void {
    this.reference.close([...this.selected()].sort((a, b) => a.localeCompare(b)));
  }
}
