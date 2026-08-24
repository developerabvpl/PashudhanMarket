import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { FieldErrors } from '@upbazaar/ui';
import { AuthStore } from '../auth-store';

/**
 * Sign-in for all three apps.
 *
 * The API does not issue tokens yet, so this accepts a pasted JWT and reads its claims. When
 * an identity provider lands, only the body of `submit` changes: the store, the interceptor
 * and the guards already work off whatever token is handed to `signIn`.
 *
 * It lives in libs/auth rather than in each app because the alternative is the same component
 * copied three times.
 */
@Component({
  selector: 'upb-sign-in-page',
  imports: [TranslocoPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-md px-4 py-12">
      <h1 class="text-2xl font-semibold text-ink">{{ 'signIn.title' | transloco }}</h1>

      <form class="upb-card mt-6 space-y-4 p-6" (submit)="submit($event)">
        <div>
          <label class="block text-sm font-medium text-ink" for="token">
            {{ 'signIn.tokenLabel' | transloco }}
          </label>
          <textarea
            id="token"
            name="token"
            rows="5"
            required
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 font-mono text-xs text-ink"
            [attr.aria-describedby]="errors().length > 0 ? 'token-errors' : 'token-hint'"
            [attr.aria-invalid]="errors().length > 0"
          ></textarea>
          <p id="token-hint" class="mt-1 text-sm text-ink-muted">
            {{ 'signIn.tokenHint' | transloco }}
          </p>
          <upb-field-errors fieldId="token" [errors]="errors()" />
        </div>

        <button
          type="submit"
          class="w-full rounded-control bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700"
        >
          {{ 'signIn.submit' | transloco }}
        </button>
      </form>
    </section>
  `,
})
export class SignInPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly errors = signal<readonly string[]>([]);

  submit(event: Event): void {
    event.preventDefault();

    const form = event.target as HTMLFormElement;
    const token = new FormData(form).get('token')?.toString().trim() ?? '';

    if (this.auth.signIn(token) === null) {
      this.errors.set(['signIn.invalid']);
      return;
    }

    this.errors.set([]);

    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
    void this.router.navigateByUrl(returnUrl);
  }
}
