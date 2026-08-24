import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthStore, HasPermissionDirective } from '@upbazaar/auth';
import { LanguageSwitcher, ToastHost } from '@upbazaar/ui';
import { CATALOG_PRODUCTS_WRITE } from './features/products/products.routes';

@Component({
  selector: 'upb-root',
  imports: [
    RouterOutlet,
    RouterLink,
    TranslocoPipe,
    LanguageSwitcher,
    ToastHost,
    HasPermissionDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="upb-sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-control focus:bg-surface focus:px-4 focus:py-2"
      href="#main"
    >
      {{ 'app.skipToContent' | transloco }}
    </a>

    <header class="border-b border-border bg-surface">
      <div class="mx-auto flex max-w-4xl items-center justify-between gap-4 px-4 py-3">
        <a class="text-lg font-semibold text-ink" routerLink="/products">
          {{ 'app.sellerPortal' | transloco }}
        </a>

        <nav class="flex items-center gap-4">
          <!-- Hidden rather than disabled: an affordance the API would reject is just noise. -->
          <a *hasPermission="productsWrite" class="text-ink hover:text-brand-700" routerLink="/products">
            {{ 'seller.newProduct' | transloco }}
          </a>

          <upb-language-switcher />

          @if (auth.isAuthenticated()) {
          <span class="text-sm text-ink-muted">
            {{ 'signIn.signedInAs' | transloco: { name: auth.userName() } }}
          </span>
          <button type="button" class="text-sm text-accent-600 hover:underline" (click)="auth.signOut()">
            {{ 'nav.signOut' | transloco }}
          </button>
          } @else {
          <a class="text-sm text-accent-600 hover:underline" routerLink="/sign-in">
            {{ 'nav.signIn' | transloco }}
          </a>
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
  protected readonly auth = inject(AuthStore);
  protected readonly productsWrite = CATALOG_PRODUCTS_WRITE;
}
