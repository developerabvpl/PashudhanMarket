import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '@upbazaar/auth';
import { fieldErrorsFor, toApiProblem, ApiProblem } from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';

/**
 * Creates the account a seller signs in with, then sends them to the application. The same kind
 * of account a shopper has: approval adds seller access to it rather than making a second one.
 */
@Component({
  selector: 'upb-register-page',
  imports: [RouterLink, TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-md px-4 py-10">
      <h1 class="text-2xl font-semibold text-ink">{{ 'sellerPortal.registerTitle' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'sellerPortal.registerIntro' | transloco }}</p>

      <form class="upb-card mt-6 space-y-3 p-6" (submit)="register($event)">
        <mat-form-field class="w-full">
          <mat-label>{{ 'auth.nameLabel' | transloco }}</mat-label>
          <input matInput name="displayName" autocomplete="name" required [value]="name()" (input)="name.set(value($event))" />
        </mat-form-field>
        <mat-form-field class="w-full">
          <mat-label>{{ 'auth.emailLabel' | transloco }}</mat-label>
          <input matInput name="email" type="email" autocomplete="email" required [value]="email()" (input)="email.set(value($event))" />
        </mat-form-field>
        <upb-field-errors fieldId="email" [errors]="errorsFor('email')" />
        <mat-form-field class="w-full">
          <mat-label>{{ 'auth.passwordLabel' | transloco }}</mat-label>
          <input matInput name="password" type="password" autocomplete="new-password" required minlength="12"
            [value]="password()" (input)="password.set(value($event))" />
          <mat-hint>{{ 'validation.password' | transloco }}</mat-hint>
        </mat-form-field>
        <upb-field-errors fieldId="password" [errors]="errorsFor('password')" />

        <button mat-flat-button color="primary" class="w-full" type="submit"
          [disabled]="busy() || !name().trim() || !email().trim() || password().length < 12">
          {{ (busy() ? 'common.working' : 'sellerPortal.registerSubmit') | transloco }}
        </button>

        <p class="text-center text-sm">
          <a routerLink="/sign-in">{{ 'sellerPortal.haveAccount' | transloco }}</a>
        </p>
      </form>
    </section>
  `,
})
export class RegisterPage {
  protected readonly name = signal('');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  private readonly problem = signal<ApiProblem | null>(null);

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected errorsFor(field: string): readonly string[] {
    return fieldErrorsFor(this.problem(), field);
  }

  protected async register(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.problem.set(null);

    try {
      await this.auth.register(this.email().trim(), this.password(), this.name().trim(), this.transloco.getActiveLang());
      await this.router.navigate(['/apply']);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
