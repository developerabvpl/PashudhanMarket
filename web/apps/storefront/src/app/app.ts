import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LanguageSwitcher, ToastHost } from '@upbazaar/ui';

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
        </nav>
      </div>
    </header>

    <main id="main" tabindex="-1">
      <router-outlet />
    </main>

    <upb-toast-host />
  `,
})
export class App {}
