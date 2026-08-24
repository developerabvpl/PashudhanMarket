import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { LanguageSwitcher, ToastHost, ToastService } from '@upbazaar/ui';
import { AuthSheetService } from './features/auth/auth-sheet.service';

@Component({
  selector: 'upb-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoPipe, LanguageSwitcher, ToastHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="upb-sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-control focus:bg-surface focus:px-4 focus:py-2 focus:shadow-raised"
      href="#main"
    >
      {{ 'app.skipToContent' | transloco }}
    </a>

    <header class="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:gap-6">
        <a class="flex shrink-0 items-center gap-2" routerLink="/products">
          <span
            class="grid h-9 w-9 place-items-center rounded-card bg-brand-600 text-base font-bold text-white"
            aria-hidden="true"
          >
            उ
          </span>
          <span class="hidden text-base font-semibold tracking-tight text-ink sm:block">
            {{ 'app.storefront' | transloco }}
          </span>
        </a>

        <nav
          class="hidden shrink-0 items-center gap-1 md:flex"
          [attr.aria-label]="'nav.primary' | transloco"
        >
          <a
            class="rounded-control px-3 py-2 text-sm font-medium text-ink-muted transition-colors hover:bg-surface-sunken hover:text-ink"
            routerLink="/products"
            routerLinkActive="bg-surface-sunken text-ink"
          >
            {{ 'nav.products' | transloco }}
          </a>
        </nav>

        <div class="flex flex-1 items-center justify-end gap-2 sm:gap-3">
          <upb-language-switcher [reloadOnSwitch]="true" />

          @if (displayName(); as name) {
          <div class="relative">
            <button
              type="button"
              class="flex items-center gap-2 rounded-control border border-border px-2.5 py-1.5 text-sm font-medium text-ink transition-colors hover:bg-surface-sunken sm:px-3"
              [attr.aria-expanded]="menuOpen()"
              aria-haspopup="menu"
              (click)="toggleMenu()"
            >
              <span
                class="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-brand-100 text-xs font-semibold text-brand-800"
                aria-hidden="true"
              >
                {{ initial() }}
              </span>
              <span class="hidden max-w-32 truncate sm:block">{{ name }}</span>
            </button>

            @if (menuOpen()) {
            <div
              class="upb-card absolute right-0 z-20 mt-2 w-52 p-1.5 text-sm"
              role="menu"
              (mouseleave)="menuOpen.set(false)"
            >
              <a
                class="block rounded-control px-3 py-2 text-ink transition-colors hover:bg-surface-sunken"
                role="menuitem"
                routerLink="/account"
                (click)="menuOpen.set(false)"
              >
                {{ 'nav.account' | transloco }}
              </a>
              <button
                type="button"
                class="block w-full rounded-control px-3 py-2 text-left text-ink transition-colors hover:bg-surface-sunken"
                role="menuitem"
                (click)="signOut()"
              >
                {{ 'nav.signOut' | transloco }}
              </button>
            </div>
            }
          </div>
          } @else {
          <button
            type="button"
            class="rounded-control bg-brand-600 px-3.5 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-700 sm:px-4"
            (click)="openSignIn()"
          >
            {{ 'nav.signIn' | transloco }}
          </button>
          }
        </div>
      </div>
    </header>

    <main id="main" tabindex="-1" class="min-h-[60vh]">
      <router-outlet />
    </main>

    <footer class="mt-16 border-t border-border bg-surface">
      <div class="mx-auto grid max-w-7xl gap-8 px-4 py-12 sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <p class="text-sm font-semibold text-ink">{{ 'app.storefront' | transloco }}</p>
          <p class="mt-2 max-w-xs text-sm leading-relaxed text-ink-muted">
            {{ 'footer.tagline' | transloco }}
          </p>
        </div>

        <nav [attr.aria-label]="'footer.shop' | transloco">
          <p class="text-sm font-semibold text-ink">{{ 'footer.shop' | transloco }}</p>
          <ul class="mt-3 space-y-2 text-sm">
            <li>
              <a class="text-ink-muted transition-colors hover:text-ink" routerLink="/products">
                {{ 'nav.products' | transloco }}
              </a>
            </li>
            <li>
              <a class="text-ink-muted transition-colors hover:text-ink" routerLink="/account">
                {{ 'nav.account' | transloco }}
              </a>
            </li>
          </ul>
        </nav>

        <div>
          <p class="text-sm font-semibold text-ink">{{ 'footer.sell' | transloco }}</p>
          <p class="mt-3 text-sm leading-relaxed text-ink-muted">
            {{ 'footer.sellBody' | transloco }}
          </p>
        </div>

        <div>
          <p class="text-sm font-semibold text-ink">{{ 'footer.help' | transloco }}</p>
          <p class="mt-3 text-sm leading-relaxed text-ink-muted">
            {{ 'footer.helpBody' | transloco }}
          </p>
        </div>
      </div>

      <div class="border-t border-border">
        <p class="mx-auto max-w-7xl px-4 py-6 text-xs text-ink-muted">
          {{ 'footer.copyright' | transloco }}
        </p>
      </div>
    </footer>

    <upb-toast-host />
  `,
})
export class App {
  private readonly sheet = inject(AuthSheetService);
  private readonly auth = inject(AuthService);
  private readonly currentUser = inject(CurrentUserStore);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly displayName = this.currentUser.displayName;
  protected readonly menuOpen = signal(false);

  constructor() {
    // A visitor arriving with a stored session should see their name in the header without
    // having to touch a guarded route first.
    void this.currentUser.ensureLoaded();
  }

  /** First character of the display name, for the avatar disc. */
  protected initial(): string {
    return (this.displayName() ?? '').trim().charAt(0).toUpperCase();
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected async openSignIn(): Promise<void> {
    const signedIn = await this.sheet.open();

    if (signedIn) {
      this.toast.success('auth.welcome');
    }
  }

  protected async signOut(): Promise<void> {
    this.menuOpen.set(false);

    await this.auth.logout();

    this.toast.info('auth.signedOut');

    await this.router.navigate(['/products']);
  }
}
