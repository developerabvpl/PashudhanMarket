import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toApiProblem } from '@upbazaar/data-access';
import { AuthService } from '../auth.service';

/**
 * Email and password sign-in for the seller and admin portals.
 *
 * Typed ReactiveForms rather than Signal Forms: Material's form-field integration is built
 * around ControlValueAccessor, which is what `formControlName` speaks. The storefront, whose
 * inputs are plain elements, uses Signal Forms.
 *
 * A staff account with TOTP enabled does not get tokens here — it gets a challenge token, and
 * this page hands over to the two-factor screen.
 */
@Component({
  selector: 'upb-portal-sign-in-page',
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
            The space under the title is on the heading itself: Material's own CSS sets the header's and the
            content's padding (none between them) and outranks Tailwind's utilities on its elements, so the
            title sat flush against the first field.
          -->
          <h1 class="pb-4 text-xl font-semibold">{{ title() | transloco }}</h1>
        </mat-card-header>

        <mat-card-content class="p-6">
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field class="w-full">
              <mat-label>{{ 'auth.emailLabel' | transloco }}</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="username" />
              @if (form.controls.email.touched && form.controls.email.invalid) {
              <mat-error>{{ 'validation.email' | transloco }}</mat-error>
              }
            </mat-form-field>

            <mat-form-field class="mt-2 w-full">
              <mat-label>{{ 'auth.passwordLabel' | transloco }}</mat-label>
              <input
                matInput
                type="password"
                formControlName="password"
                autocomplete="current-password"
              />
              @if (form.controls.password.touched && form.controls.password.invalid) {
              <mat-error>{{ 'validation.required' | transloco }}</mat-error>
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
              {{ (busy() ? 'auth.signingIn' : 'auth.signIn') | transloco }}
            </button>
          </form>

          <a class="mt-4 block text-center text-sm" routerLink="/forgot-password">
            {{ 'auth.forgotPassword' | transloco }}
          </a>

          @if (registerUrl(); as url) {
          <a class="mt-2 block text-center text-sm" [routerLink]="url">{{ 'auth.noAccount' | transloco }}</a>
          }
        </mat-card-content>
      </mat-card>
    </section>
  `,
})
export class PortalSignInPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  /** i18n key for the heading, so each portal can name itself. */
  readonly title = input('app.sellerPortal');

  /** Where to land after a successful sign-in, when there is no return URL. */
  readonly defaultReturnUrl = input('/');

  /**
   * Where to create an account, for a portal anyone may join - the seller portal, where a new
   * seller signs up and then applies. Unset for the admin portal, whose accounts staff create.
   */
  readonly registerUrl = input<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);

    const { email, password } = this.form.getRawValue();

    try {
      const result = await this.auth.login(email, password);

      if (result.requiresTwoFactor && result.twoFactorToken) {
        // The password was right; the session is only half open. Carry the challenge token
        // through navigation state so it never lands in a URL or the browser history.
        await this.router.navigate(['/two-factor'], {
          state: { twoFactorToken: result.twoFactorToken, returnUrl: this.returnUrl() },
        });

        return;
      }

      await this.router.navigateByUrl(this.returnUrl());
    } catch (error) {
      const problem = toApiProblem(error);

      this.formError.set(
        problem.title.startsWith('errors.') ? this.transloco.translate(problem.title) : problem.title
      );
    } finally {
      this.busy.set(false);
    }
  }

  private returnUrl(): string {
    return this.route.snapshot.queryParamMap.get('returnUrl') ?? this.defaultReturnUrl();
  }
}
