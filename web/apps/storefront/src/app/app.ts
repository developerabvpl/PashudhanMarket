import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { LanguageSwitcher, ToastHost, ToastService } from '@upbazaar/ui';
import { AuthSheetService } from './features/auth/auth-sheet.service';

@Component({
  selector: 'upb-root',
  imports: [RouterOutlet, RouterLink, TranslocoPipe, LanguageSwitcher, ToastHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="upb-sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-control focus:bg-surface focus:px-4 focus:py-2"
      href="#main"
    >
      {{ 'app.skipToContent' | transloco }}
    </a>

    <header class="border-b border-border bg-surface">
      <div class="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3">
        <a class="text-lg font-semibold text-ink" routerLink="/products">
          {{ 'app.storefront' | transloco }}
        </a>

        <nav class="flex items-center gap-4" [attr.aria-label]="'nav.products' | transloco">
          <a class="text-ink hover:text-brand-700" routerLink="/products">
            {{ 'nav.products' | transloco }}
          </a>

          <upb-language-switcher [reloadOnSwitch]="true" />

          @if (displayName(); as name) {
          <!-- Signed in: a small disclosure menu rather than a row of links. -->
          <div class="relative">
            <button
              type="button"
              class="rounded-control border border-border px-3 py-1.5 text-sm text-ink"
              [attr.aria-expanded]="menuOpen()"
              aria-haspopup="menu"
              (click)="toggleMenu()"
            >
              {{ name }}
            </button>

            @if (menuOpen()) {
            <div
              class="upb-card absolute right-0 z-20 mt-1 w-48 p-1 text-sm"
              role="menu"
              (mouseleave)="menuOpen.set(false)"
            >
              <a
                class="block rounded-control px-3 py-2 text-ink hover:bg-surface-sunken"
                role="menuitem"
                routerLink="/account"
                (click)="menuOpen.set(false)"
              >
                {{ 'nav.account' | transloco }}
              </a>
              <button
                type="button"
                class="block w-full rounded-control px-3 py-2 text-left text-ink hover:bg-surface-sunken"
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
            class="rounded-control bg-brand-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-700"
            (click)="openSignIn()"
          >
            {{ 'nav.signIn' | transloco }}
          </button>
          }
        </nav>
      </div>
    </header>

    <main id="main" tabindex="-1">
      <router-outlet />
    </main>

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
