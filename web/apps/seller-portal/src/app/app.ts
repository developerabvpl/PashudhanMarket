import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, CurrentUserStore, HasPermissionDirective } from '@upbazaar/auth';
import { LanguageSwitcher, ToastHost, ToastService } from '@upbazaar/ui';
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
    MatToolbarModule,
    MatButtonModule,
    MatMenuModule,
    MatIconModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="upb-sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-control focus:bg-surface focus:px-4 focus:py-2"
      href="#main"
    >
      {{ 'app.skipToContent' | transloco }}
    </a>

    @if (signedIn()) {
    <mat-toolbar color="primary">
      <a class="font-semibold" routerLink="/products">{{ 'app.sellerPortal' | transloco }}</a>

      <span class="flex-1"></span>

      <!-- Hidden rather than disabled: an affordance the API would reject is just noise. -->
      <a *hasPermission="productsWrite" mat-button routerLink="/products">
        {{ 'seller.newProduct' | transloco }}
      </a>

      <upb-language-switcher />

      <button mat-button [matMenuTriggerFor]="accountMenu">{{ displayName() }}</button>
      <mat-menu #accountMenu="matMenu">
        <a mat-menu-item routerLink="/change-password">
          {{ 'changePassword.title' | transloco }}
        </a>
        <button mat-menu-item type="button" (click)="signOut()">
          {{ 'nav.signOut' | transloco }}
        </button>
      </mat-menu>
    </mat-toolbar>
    }

    <main id="main" tabindex="-1">
      <router-outlet />
    </main>

    <upb-toast-host />
  `,
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly currentUser = inject(CurrentUserStore);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly productsWrite = CATALOG_PRODUCTS_WRITE;
  protected readonly signedIn = this.currentUser.isSignedIn;
  protected readonly displayName = this.currentUser.displayName;

  constructor() {
    // Restores the header for someone returning with a stored session, without waiting for a
    // guarded route to run.
    void this.currentUser.ensureLoaded();
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();

    this.toast.info('auth.signedOut');

    await this.router.navigate(['/sign-in']);
  }
}
