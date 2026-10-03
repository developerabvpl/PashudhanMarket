import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toApiProblem } from '@upbazaar/data-access';
import { AuthService } from '../auth.service';

/**
 * The second half of a staff sign-in.
 *
 * The challenge token arrives through navigation state rather than the URL: it is a bearer
 * credential for the next five minutes, and a query string ends up in history, in server logs
 * and in whatever the user pastes into a support chat. Arriving here without one means the
 * page was opened directly, so it sends the visitor back to sign in.
 */
@Component({
  selector: 'upb-two-factor-challenge-page',
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
            <h1 class="text-xl font-semibold">{{ 'twoFactor.challengeTitle' | transloco }}</h1>
            <p class="mt-1 text-sm text-ink-muted">{{ 'twoFactor.challengeBody' | transloco }}</p>
          </div>
        </mat-card-header>

        <mat-card-content class="p-6">
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field class="w-full">
              <mat-label>{{ 'twoFactor.codeLabel' | transloco }}</mat-label>
              <input
                matInput
                type="text"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="6"
                formControlName="code"
              />
              @if (form.controls.code.touched && form.controls.code.invalid) {
              <mat-error>{{ 'validation.totpCode' | transloco }}</mat-error>
              }
            </mat-form-field>

            @if (formError(); as message) {
            <p class="mt-2 text-sm text-danger" role="alert">{{ message }}</p>
            }

            <button
              mat-flat-button
              color="primary"
              type="submit"
              class="mt-4 w-full"
              [disabled]="busy()"
            >
              {{ (busy() ? 'auth.verifying' : 'twoFactor.verify') | transloco }}
            </button>
          </form>
        </mat-card-content>
      </mat-card>
    </section>
  `,
})
export class TwoFactorChallengePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  private readonly navigation = this.router.getCurrentNavigation()?.extras.state
    ?? (typeof history === 'undefined' ? undefined : history.state);

  private readonly twoFactorToken = this.navigation?.['twoFactorToken'] as string | undefined;
  private readonly returnUrl = (this.navigation?.['returnUrl'] as string | undefined) ?? '/';

  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[0-9]{6}$/)]],
  });

  constructor() {
    if (!this.twoFactorToken) {
      void this.router.navigate(['/sign-in']);
    }
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.busy() || !this.twoFactorToken) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);

    try {
      await this.auth.verifyTwoFactor(this.twoFactorToken, this.form.getRawValue().code);

      await this.router.navigateByUrl(this.returnUrl);
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
