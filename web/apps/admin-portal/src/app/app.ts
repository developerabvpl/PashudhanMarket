import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, CurrentUserStore, HasPermissionDirective } from '@upbazaar/auth';
import { LanguageSwitcher, ToastHost, ToastService } from '@upbazaar/ui';
import {
  CatalogPermissions,
  IdentityPermissions,
  OrderingPermissions,
  PaymentsPermissions,
  SellersPermissions,
  ShippingPermissions,
} from './core/permissions';

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
      <a class="font-semibold" routerLink="/staff">{{ 'app.adminPortal' | transloco }}</a>

      <span class="flex-1"></span>

      <!--
        Each entry is gated by the permission its route requires, so the menu never offers a
        page that would answer 403. The guard still enforces it; this only keeps the UI honest.
      -->
      <a *hasPermission="usersRead" mat-button routerLink="/staff">
        {{ 'nav.staffUsers' | transloco }}
      </a>
      <a *hasPermission="ordersRead" mat-button routerLink="/orders">
        {{ 'nav.orders' | transloco }}
      </a>
      <a *hasPermission="paymentsRead" mat-button routerLink="/payments">
        {{ 'nav.payments' | transloco }}
      </a>
      <a *hasPermission="shipmentsRead" mat-button routerLink="/shipping">
        {{ 'nav.shipments' | transloco }}
      </a>
      <a *hasPermission="sellersRead" mat-button routerLink="/sellers">
        {{ 'nav.sellers' | transloco }}
      </a>
      <a *hasPermission="productsWrite" mat-button routerLink="/catalog/review">
        {{ 'nav.listingReview' | transloco }}
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

  protected readonly usersRead = IdentityPermissions.UsersRead;
  protected readonly ordersRead = OrderingPermissions.Read;
  protected readonly paymentsRead = PaymentsPermissions.Read;
  protected readonly shipmentsRead = ShippingPermissions.ShipmentsRead;
  protected readonly sellersRead = SellersPermissions.Read;
  protected readonly productsWrite = CatalogPermissions.ProductsWrite;

  protected readonly signedIn = this.currentUser.isSignedIn;
  protected readonly displayName = this.currentUser.displayName;

  constructor() {
    void this.currentUser.ensureLoaded();
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();

    this.toast.info('auth.signedOut');

    await this.router.navigate(['/sign-in']);
  }
}
