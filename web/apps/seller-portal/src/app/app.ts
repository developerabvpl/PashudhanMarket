import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { LanguageSwitcher, ToastHost, ToastService } from '@upbazaar/ui';
import { SellerAccess } from './core/seller-access';

@Component({
  selector: 'upb-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    LanguageSwitcher,
    ToastHost,
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
      <a class="font-semibold" routerLink="/orders">{{ 'app.sellerPortal' | transloco }}</a>

      <span class="flex-1"></span>

      <!-- A seller not yet approved has only their application to look at. -->
      @if (canSell()) {
      <a mat-button routerLink="/orders" routerLinkActive="!bg-white/10" [routerLinkActiveOptions]="{ exact: true }">{{ 'sellerPortal.ordersTitle' | transloco }}</a>
      <a mat-button routerLink="/orders/returns" routerLinkActive="!bg-white/10">{{ 'nav.returns' | transloco }}</a>
      <a mat-button routerLink="/products" routerLinkActive="!bg-white/10">{{ 'sellerPortal.productsTitle' | transloco }}</a>
      <a mat-button routerLink="/settings" routerLinkActive="!bg-white/10">{{ 'sellerPortal.settingsTitle' | transloco }}</a>
      } @else {
      <a mat-button routerLink="/apply">{{ 'sellerPortal.applyTitle' | transloco }}</a>
      }

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
  private readonly access = inject(SellerAccess);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly signedIn = this.currentUser.isSignedIn;
  protected readonly displayName = this.currentUser.displayName;
  protected readonly canSell = this.access.canSell;

  constructor() {
    // Restores the header for someone returning with a stored session, without waiting for a
    // guarded route to run.
    void this.currentUser.ensureLoaded().then((user) => (user ? this.access.load() : null));
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.access.clear();

    this.toast.info('auth.signedOut');

    await this.router.navigate(['/sign-in']);
  }
}
