import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '@upbazaar/auth';
import { toApiProblem } from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';
import { isMobile, normalizeMobile } from '@upbazaar/util';

type Tab = 'mobile' | 'email';
type MobileStep = 'number' | 'code';
type EmailMode = 'signIn' | 'register';

/** Seconds before a new code may be requested. */
const RESEND_SECONDS = 30;

/**
 * The sign-in form, shared by the bottom sheet and the full-page route.
 *
 * Mobile first, because that is how most buyers here will actually sign in: no password to
 * remember, and verifying the number creates the account, so there is no separate sign-up to
 * complete. Email and password sit behind the second tab for people who already have one.
 */
@Component({
  selector: 'upb-auth-panel',
  imports: [TranslocoPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-5">
      <header>
        <h1 class="text-xl font-semibold text-ink">{{ 'auth.title' | transloco }}</h1>
        <p class="mt-1 text-sm text-ink-muted">{{ 'auth.subtitle' | transloco }}</p>
      </header>

      <div class="flex gap-1 rounded-control bg-surface-sunken p-1" role="tablist">
        @for (option of tabs; track option) {
        <button
          type="button"
          role="tab"
          class="flex-1 rounded-control px-3 py-2 text-sm font-medium"
          [class.bg-surface]="tab() === option"
          [class.text-ink]="tab() === option"
          [class.text-ink-muted]="tab() !== option"
          [attr.aria-selected]="tab() === option"
          (click)="selectTab(option)"
        >
          {{ (option === 'mobile' ? 'auth.tabMobile' : 'auth.tabEmail') | transloco }}
        </button>
        }
      </div>

      @if (tab() === 'mobile') {
      <!-- Mobile: number, then code. -->
      @if (mobileStep() === 'number') {
      <form class="space-y-4" (submit)="sendCode($event)">
        <div>
          <label class="block text-sm font-medium text-ink" for="mobile">
            {{ 'auth.mobileLabel' | transloco }}
          </label>
          <div class="mt-1 flex items-center gap-2">
            <span class="rounded-control bg-surface-sunken px-3 py-2 text-sm text-ink-muted">
              {{ 'auth.mobilePrefix' | transloco }}
            </span>
            <input
              id="mobile"
              name="mobile"
              type="tel"
              inputmode="numeric"
              autocomplete="tel-national"
              maxlength="10"
              class="w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
              [value]="mobile()"
              [attr.aria-invalid]="mobileErrors().length > 0"
              [attr.aria-describedby]="mobileErrors().length > 0 ? 'mobile-errors' : 'mobile-hint'"
              (input)="mobile.set(value($event))"
            />
          </div>
          <p id="mobile-hint" class="mt-1 text-sm text-ink-muted">
            {{ 'auth.mobileHint' | transloco }}
          </p>
          <upb-field-errors fieldId="mobile" [errors]="mobileErrors()" />
        </div>

        <button
          type="submit"
          class="w-full rounded-control bg-brand-600 px-4 py-2.5 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="busy()"
        >
          {{ (busy() ? 'auth.sendingCode' : 'auth.sendCode') | transloco }}
        </button>
      </form>
      } @else {
      <form class="space-y-4" (submit)="verifyCode($event)">
        <p class="text-sm text-ink" aria-live="polite">
          {{ 'auth.codeSentTo' | transloco: { mobile: '+91 ' + mobile() } }}
          <button type="button" class="ml-1 text-accent-600 underline" (click)="editNumber()">
            {{ 'auth.changeNumber' | transloco }}
          </button>
        </p>

        <div>
          <label class="block text-sm font-medium text-ink" for="code">
            {{ 'auth.codeLabel' | transloco }}
          </label>
          <input
            id="code"
            name="code"
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            maxlength="6"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-center font-mono text-lg tracking-[0.5em] text-ink"
            [value]="code()"
            [attr.aria-invalid]="codeErrors().length > 0"
            [attr.aria-describedby]="codeErrors().length > 0 ? 'code-errors' : null"
            (input)="code.set(value($event))"
          />
          <upb-field-errors fieldId="code" [errors]="codeErrors()" />
        </div>

        <div>
          <label class="block text-sm font-medium text-ink" for="displayName">
            {{ 'auth.nameLabel' | transloco }}
            <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span>
          </label>
          <input
            id="displayName"
            name="displayName"
            type="text"
            autocomplete="name"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [value]="displayName()"
            (input)="displayName.set(value($event))"
          />
          <p class="mt-1 text-sm text-ink-muted">{{ 'auth.nameHint' | transloco }}</p>
        </div>

        <button
          type="submit"
          class="w-full rounded-control bg-brand-600 px-4 py-2.5 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="busy()"
        >
          {{ (busy() ? 'auth.verifying' : 'auth.verify') | transloco }}
        </button>

        <button
          type="button"
          class="w-full rounded-control px-4 py-2 text-sm text-accent-600 disabled:text-ink-muted"
          [disabled]="resendSeconds() > 0 || busy()"
          (click)="resend()"
        >
          {{
            resendSeconds() > 0
              ? ('auth.resendIn' | transloco: { seconds: resendSeconds() })
              : ('auth.resend' | transloco)
          }}
        </button>
      </form>
      } } @else {
      <!-- Email and password. -->
      <form class="space-y-4" (submit)="submitEmail($event)">
        @if (emailMode() === 'register') {
        <div>
          <label class="block text-sm font-medium text-ink" for="registerName">
            {{ 'auth.nameLabel' | transloco }}
          </label>
          <input
            id="registerName"
            name="registerName"
            type="text"
            autocomplete="name"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [value]="displayName()"
            (input)="displayName.set(value($event))"
          />
          <upb-field-errors fieldId="registerName" [errors]="nameErrors()" />
        </div>
        }

        <div>
          <label class="block text-sm font-medium text-ink" for="email">
            {{ 'auth.emailLabel' | transloco }}
          </label>
          <input
            id="email"
            name="email"
            type="email"
            autocomplete="email"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [value]="email()"
            [attr.aria-invalid]="emailErrors().length > 0"
            (input)="email.set(value($event))"
          />
          <upb-field-errors fieldId="email" [errors]="emailErrors()" />
        </div>

        <div>
          <label class="block text-sm font-medium text-ink" for="password">
            {{ 'auth.passwordLabel' | transloco }}
          </label>
          <input
            id="password"
            name="password"
            type="password"
            [attr.autocomplete]="emailMode() === 'register' ? 'new-password' : 'current-password'"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [value]="password()"
            [attr.aria-invalid]="passwordErrors().length > 0"
            (input)="password.set(value($event))"
          />
          <upb-field-errors fieldId="password" [errors]="passwordErrors()" />
        </div>

        <button
          type="submit"
          class="w-full rounded-control bg-brand-600 px-4 py-2.5 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="busy()"
        >
          {{ submitLabel() | transloco }}
        </button>

        <p class="text-center text-sm text-ink-muted">
          {{ (emailMode() === 'signIn' ? 'auth.noAccount' : 'auth.haveAccount') | transloco }}
          <button type="button" class="ml-1 text-accent-600 underline" (click)="toggleEmailMode()">
            {{ (emailMode() === 'signIn' ? 'auth.register' : 'auth.signIn') | transloco }}
          </button>
        </p>
      </form>
      }

      @if (formError(); as message) {
      <p class="rounded-control bg-danger/10 px-3 py-2 text-sm text-danger" role="alert">
        {{ message }}
      </p>
      }
    </div>
  `,
})
export class AuthPanel implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  /** Emitted once the visitor is signed in, so the host can close or navigate. */
  readonly signedIn = output<void>();

  protected readonly tabs: readonly Tab[] = ['mobile', 'email'];

  protected readonly tab = signal<Tab>('mobile');
  protected readonly mobileStep = signal<MobileStep>('number');
  protected readonly emailMode = signal<EmailMode>('signIn');

  protected readonly mobile = signal('');
  protected readonly code = signal('');
  protected readonly displayName = signal('');
  protected readonly email = signal('');
  protected readonly password = signal('');

  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly resendSeconds = signal(0);

  private readonly submitted = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  protected readonly mobileErrors = computed(() =>
    this.submitted() && !isMobile(this.mobile()) ? ['validation.mobile'] : []);

  protected readonly codeErrors = computed(() =>
    this.submitted() && !/^[0-9]{6}$/.test(this.code()) ? ['validation.otpCode'] : []);

  protected readonly emailErrors = computed(() =>
    this.submitted() && !/^\S+@\S+\.\S+$/.test(this.email()) ? ['validation.email'] : []);

  protected readonly passwordErrors = computed(() => {
    if (!this.submitted()) {
      return [];
    }

    if (this.emailMode() === 'register' && this.password().length < 12) {
      return ['validation.password'];
    }

    return this.password().length === 0 ? ['validation.required'] : [];
  });

  protected readonly nameErrors = computed(() =>
    this.submitted() && this.emailMode() === 'register' && this.displayName().trim().length === 0
      ? ['validation.required']
      : []);

  protected readonly submitLabel = computed(() => {
    if (this.busy()) {
      return this.emailMode() === 'register' ? 'auth.registering' : 'auth.signingIn';
    }

    return this.emailMode() === 'register' ? 'auth.register' : 'auth.signIn';
  });

  ngOnDestroy(): void {
    this.stopTimer();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected selectTab(tab: Tab): void {
    this.tab.set(tab);
    this.reset();
  }

  protected toggleEmailMode(): void {
    this.emailMode.update((mode) => (mode === 'signIn' ? 'register' : 'signIn'));
    this.reset();
  }

  protected editNumber(): void {
    this.mobileStep.set('number');
    this.code.set('');
    this.reset();
    this.stopTimer();
  }

  protected async sendCode(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);

    // Normalised first, so a pasted "+91 98765 43210" is accepted rather than rejected.
    this.mobile.set(normalizeMobile(this.mobile()));

    if (!isMobile(this.mobile())) {
      return;
    }

    await this.run(async () => {
      await this.auth.requestOtp(this.mobile());

      this.mobileStep.set('code');
      this.submitted.set(false);
      this.startResendTimer();
    });
  }

  protected async resend(): Promise<void> {
    await this.run(async () => {
      await this.auth.requestOtp(this.mobile());
      this.startResendTimer();
    });
  }

  protected async verifyCode(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);

    if (!/^[0-9]{6}$/.test(this.code())) {
      return;
    }

    await this.run(async () => {
      const name = this.displayName().trim();
      await this.auth.verifyOtp(this.mobile(), this.code(), name.length > 0 ? name : undefined);

      this.signedIn.emit();
    });
  }

  protected async submitEmail(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);

    const invalid = this.emailErrors().length > 0
      || this.passwordErrors().length > 0
      || this.nameErrors().length > 0;

    if (invalid) {
      return;
    }

    await this.run(async () => {
      if (this.emailMode() === 'register') {
        await this.auth.register(
          this.email(),
          this.password(),
          this.displayName().trim(),
          this.transloco.getActiveLang()
        );
      } else {
        const result = await this.auth.login(this.email(), this.password());

        if (result.requiresTwoFactor) {
          // Buyers never have TOTP; a staff account signing in here is told where to go
          // rather than being left on a screen that cannot complete.
          this.formError.set(this.transloco.translate('errors.forbidden'));

          return;
        }
      }

      this.signedIn.emit();
    });
  }

  /** Runs an action with the busy flag and error surface handled once. */
  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);

    try {
      await action();
    } catch (error) {
      const problem = toApiProblem(error);

      // Titles from the API are already human-readable; keys fall back through i18n.
      this.formError.set(
        problem.title.startsWith('errors.')
          ? this.transloco.translate(problem.title)
          : problem.title
      );
    } finally {
      this.busy.set(false);
    }
  }

  private startResendTimer(): void {
    this.stopTimer();
    this.resendSeconds.set(RESEND_SECONDS);

    if (typeof window === 'undefined') {
      return;
    }

    this.timer = setInterval(() => {
      this.resendSeconds.update((seconds) => Math.max(0, seconds - 1));

      if (this.resendSeconds() === 0) {
        this.stopTimer();
      }
    }, 1000);

    this.destroyRef.onDestroy(() => this.stopTimer());
  }

  private stopTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  private reset(): void {
    this.submitted.set(false);
    this.formError.set(null);
  }
}
