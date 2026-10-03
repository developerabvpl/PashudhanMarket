import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toApiProblem } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { AuthService } from '../auth.service';

/**
 * Starts a password reset.
 *
 * Reports the same thing whether or not the address is registered, matching the API. Saying
 * "no such account" here would turn the form into a way to test which addresses exist.
 */
@Component({
  selector: 'upb-forgot-password-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto flex min-h-dvh max-w-md items-center px-4 py-10">
      <mat-card class="w-full">
        @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
        }

        <mat-card-header class="p-6 pb-0">
          <!--
            One child: Material lays the header out as a row, and its CSS outranks Tailwind's. For the same
            reason the space under the heading is on this wrapper, not on the header or the content.
          -->
          <div class="pb-4">
            <h1 class="text-xl font-semibold">{{ 'forgotPassword.title' | transloco }}</h1>
            <p class="mt-1 text-sm text-ink-muted">{{ 'forgotPassword.body' | transloco }}</p>
          </div>
        </mat-card-header>

        <mat-card-content class="p-6">
          @if (sent()) {
          <p class="text-sm text-success" role="status">{{ 'forgotPassword.sent' | transloco }}</p>
          } @else {
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field class="w-full">
              <mat-label>{{ 'auth.emailLabel' | transloco }}</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="email" />
              @if (form.controls.email.touched && form.controls.email.invalid) {
              <mat-error>{{ 'validation.email' | transloco }}</mat-error>
              }
            </mat-form-field>

            <button
              mat-flat-button
              color="primary"
              type="submit"
              class="mt-2 w-full"
              [disabled]="busy()"
            >
              {{ (busy() ? 'common.working' : 'forgotPassword.submit') | transloco }}
            </button>
          </form>
          }

          <a class="mt-4 block text-center text-sm" routerLink="/sign-in">
            {{ 'forgotPassword.backToSignIn' | transloco }}
          </a>
        </mat-card-content>
      </mat-card>
    </section>
  `,
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.auth.forgotPassword(this.form.getRawValue().email);
    } catch {
      // Even a failure is reported as success: the endpoint must not distinguish.
    } finally {
      this.busy.set(false);
      this.sent.set(true);
    }
  }
}

/** Confirms the two new-password fields agree before anything is sent. */
function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const next = group.get('newPassword')?.value;
  const confirmation = group.get('confirmPassword')?.value;

  return next === confirmation ? null : { passwordMismatch: true };
}

/** Changes the signed-in user's password. */
@Component({
  selector: 'upb-change-password-page',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-md px-4 py-8">
      <mat-card>
        @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
        }

        <mat-card-header class="p-6 pb-0">
          <!--
            One child: Material lays the header out as a row, and its CSS outranks Tailwind's. For the same
            reason the space under the heading is on this wrapper, not on the header or the content.
          -->
          <div class="pb-4">
            <h1 class="text-xl font-semibold">{{ 'changePassword.title' | transloco }}</h1>
            <p class="mt-1 text-sm text-ink-muted">{{ 'changePassword.body' | transloco }}</p>
          </div>
        </mat-card-header>

        <mat-card-content class="p-6">
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field class="w-full">
              <mat-label>{{ 'auth.currentPasswordLabel' | transloco }}</mat-label>
              <input
                matInput
                type="password"
                formControlName="currentPassword"
                autocomplete="current-password"
              />
            </mat-form-field>

            <mat-form-field class="mt-2 w-full">
              <mat-label>{{ 'auth.newPasswordLabel' | transloco }}</mat-label>
              <input
                matInput
                type="password"
                formControlName="newPassword"
                autocomplete="new-password"
              />
              @if (form.controls.newPassword.touched && form.controls.newPassword.invalid) {
              <mat-error>{{ 'validation.password' | transloco }}</mat-error>
              }
            </mat-form-field>

            <mat-form-field class="mt-2 w-full">
              <mat-label>{{ 'auth.confirmPasswordLabel' | transloco }}</mat-label>
              <input
                matInput
                type="password"
                formControlName="confirmPassword"
                autocomplete="new-password"
              />
            </mat-form-field>

            @if (form.touched && form.hasError('passwordMismatch')) {
            <p class="text-sm text-danger" role="alert">
              {{ 'validation.passwordsDiffer' | transloco }}
            </p>
            } @if (formError(); as message) {
            <p class="mt-2 text-sm text-danger" role="alert">{{ message }}</p>
            }

            <button
              mat-flat-button
              color="primary"
              type="submit"
              class="mt-4 w-full"
              [disabled]="busy()"
            >
              {{ (busy() ? 'common.working' : 'changePassword.submit') | transloco }}
            </button>
          </form>
        </mat-card-content>
      </mat-card>
    </section>
  `,
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly transloco = inject(TranslocoService);

  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(12)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatch }
  );

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);

    const { currentPassword, newPassword } = this.form.getRawValue();

    try {
      await this.auth.changePassword(currentPassword, newPassword);

      this.toast.success('changePassword.changed');
      this.form.reset();
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
