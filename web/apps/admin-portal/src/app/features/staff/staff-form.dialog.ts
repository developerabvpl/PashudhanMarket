import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { RoleDto, UserSummaryDto, toApiProblem } from '@upbazaar/data-access';
import { StaffService } from './staff.service';

/** Create when `user` is absent, edit when it is present. */
export interface StaffFormData {
  readonly user?: UserSummaryDto;
  readonly roles: readonly RoleDto[];
}

/**
 * Creates or edits a staff account.
 *
 * Email is set once, at creation: it is the sign-in name, and changing it silently would move
 * somebody's account out from under them. Roles are edited separately, because widening
 * access deserves its own confirmation rather than riding along with a name change.
 */
@Component({
  selector: 'upb-staff-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>
      {{
        isEdit
          ? ('staff.editTitle' | transloco: { name: data.user?.displayName })
          : ('staff.createTitle' | transloco)
      }}
    </h2>

    <mat-dialog-content>
      <form [formGroup]="form" class="flex flex-col pt-2">
        @if (!isEdit) {
        <mat-form-field>
          <mat-label>{{ 'auth.emailLabel' | transloco }}</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="off" />
          @if (form.controls.email.touched && form.controls.email.invalid) {
          <mat-error>{{ 'validation.email' | transloco }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field>
          <mat-label>{{ 'staff.initialPassword' | transloco }}</mat-label>
          <input matInput type="password" formControlName="password" autocomplete="new-password" />
          <mat-hint>{{ 'staff.initialPasswordHint' | transloco }}</mat-hint>
          @if (form.controls.password.touched && form.controls.password.invalid) {
          <mat-error>{{ 'validation.password' | transloco }}</mat-error>
          }
        </mat-form-field>
        }

        <mat-form-field>
          <mat-label>{{ 'account.displayName' | transloco }}</mat-label>
          <input matInput type="text" formControlName="displayName" />
          @if (form.controls.displayName.touched && form.controls.displayName.invalid) {
          <mat-error>{{ 'validation.required' | transloco }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field>
          <mat-label>{{ 'app.languageLabel' | transloco }}</mat-label>
          <mat-select formControlName="preferredLanguage">
            <mat-option value="en">{{ 'app.english' | transloco }}</mat-option>
            <mat-option value="hi">{{ 'app.hindi' | transloco }}</mat-option>
          </mat-select>
        </mat-form-field>

        @if (isEdit) {
        <mat-form-field>
          <mat-label>{{ 'staff.columnStatus' | transloco }}</mat-label>
          <mat-select formControlName="status">
            <mat-option value="Active">{{ 'staff.statusActive' | transloco }}</mat-option>
            <mat-option value="Suspended">{{ 'staff.statusSuspended' | transloco }}</mat-option>
          </mat-select>
        </mat-form-field>
        } @else {
        <mat-form-field>
          <mat-label>{{ 'account.roles' | transloco }}</mat-label>
          <mat-select formControlName="roles" multiple>
            @for (role of data.roles; track role.name) {
            <mat-option [value]="role.name">{{ role.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        }

        @if (formError(); as message) {
        <p class="text-sm text-danger" role="alert">{{ message }}</p>
        }
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button type="button" [disabled]="busy()" (click)="reference.close()">
        {{ 'common.cancel' | transloco }}
      </button>
      <button
        mat-flat-button
        color="primary"
        type="button"
        [disabled]="busy()"
        (click)="submit()"
      >
        {{ (busy() ? 'common.working' : 'common.save') | transloco }}
      </button>
    </mat-dialog-actions>
  `,
})
export class StaffFormDialog {
  private readonly staff = inject(StaffService);
  private readonly transloco = inject(TranslocoService);

  protected readonly data = inject<StaffFormData>(MAT_DIALOG_DATA);
  protected readonly reference = inject<MatDialogRef<StaffFormDialog, boolean>>(MatDialogRef);

  protected readonly isEdit = this.data.user !== undefined;
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: [{ value: this.data.user?.email ?? '', disabled: this.isEdit }, [
      Validators.required,
      Validators.email,
    ]],
    password: [
      '',
      this.isEdit ? [] : [Validators.required, Validators.minLength(12)],
    ],
    displayName: [this.data.user?.displayName ?? '', [Validators.required]],
    preferredLanguage: ['en', [Validators.required]],
    status: [this.data.user?.status ?? 'Active', [Validators.required]],
    roles: [[] as string[]],
  });

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEdit && this.data.user) {
        await this.staff.update(
          this.data.user.id,
          value.displayName.trim(),
          value.preferredLanguage,
          value.status
        );
      } else {
        await this.staff.create(
          value.email,
          value.password,
          value.displayName.trim(),
          value.preferredLanguage,
          value.roles
        );
      }

      this.reference.close(true);
    } catch (error) {
      const problem = toApiProblem(error);

      this.formError.set(
        problem.title.startsWith('errors.') ? this.transloco.translate(problem.title) : problem.title
      );
    } finally {
      this.busy.set(false);
    }
  }
}
