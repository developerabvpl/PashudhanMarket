import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, CurrentUserStore, visibleNav } from '@upbazaar/auth';
import { AccountMenu, NavCollapsedMenu, NavGroupMenu } from '@upbazaar/auth/portal';
import { LanguageSwitcher, ToastHost, ToastService } from '@upbazaar/ui';
import { SELLER_NAV } from './core/nav';
import { SellerAccess } from './core/seller-access';

@Component({
  selector: 'upb-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    AccountMenu,
    NavGroupMenu,
    NavCollapsedMenu,
    TranslocoPipe,
    LanguageSwitcher,
    ToastHost,
    MatToolbarModule,
    MatButtonModule,
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
    <mat-toolbar color="primary" class="gap-1">
      <a class="min-w-0 truncate font-semibold" routerLink="/orders">{{ 'app.sellerPortal' | transloco }}</a>

      <span class="flex-1"></span>

      <!-- A seller not yet approved has only their application to look at. -->
      @if (canSell()) {
      <!--
        Wide screens get the pages as buttons and a drop-down; below 1024px they would overlap, so
        the same pages fold into one menu. Both come from SELLER_NAV, filtered to what this
        member's role opens. The menus load once the page settles: Material's menu brings the CDK
        overlay, which the first screen does not need.
      -->
      <nav class="hidden items-center lg:flex" [attr.aria-label]="'nav.primary' | transloco">
        @for (entry of nav(); track entry.label) {
        @if (entry.kind === 'group') {
        @defer (on idle) {
        <upb-nav-group-menu [group]="entry" />
        } @placeholder {
        <button mat-button type="button">{{ entry.label | transloco }}</button>
        }
        } @else {
        <a mat-button [routerLink]="entry.route" routerLinkActive="!bg-white/10" [routerLinkActiveOptions]="{ exact: entry.exact ?? false }">
          {{ entry.label | transloco }}
        </a>
        }
        }
      </nav>

      <div class="lg:hidden">
        @defer (on idle) {
        <upb-nav-collapsed-menu [entries]="nav()" />
        } @placeholder {
        <button mat-button type="button">{{ 'nav.menu' | transloco }}</button>
        }
      </div>
      } @else {
      <a mat-button routerLink="/apply">{{ 'sellerPortal.applyTitle' | transloco }}</a>
      }

      <upb-language-switcher />

      <!-- Loaded once the page settles: the menu brings Material's overlay, which the first screen does not need. -->
      @defer (on idle) {
      <upb-account-menu [displayName]="displayName()" (signOut)="signOut()" />
      } @placeholder {
      <button mat-button type="button">{{ displayName() }}</button>
      }
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

  /** The toolbar's pages this member's role opens; recomputed when their permissions change. */
  protected readonly nav = computed(() => visibleNav(SELLER_NAV, (permission) => this.currentUser.has(permission)));

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
