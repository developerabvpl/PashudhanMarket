import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoPipe } from '@jsverse/transloco';
import { PortalNavEntry, PortalNavGroup, startsOwnSection } from '../portal-nav';

/*
 * A portal toolbar's drop-downs, as components of their own so the shell can load them with
 * @defer: Material's menu brings the CDK overlay, which nothing else on the first screen needs.
 * The shell decides which pages the user may see (visibleNav); these only draw them.
 */

/** One group's drop-down on a wide toolbar. */
@Component({
  selector: 'upb-nav-group-menu',
  imports: [RouterLink, RouterLinkActive, MatButtonModule, MatMenuModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button mat-button type="button" [matMenuTriggerFor]="menu">{{ group().label | transloco }}</button>
    <mat-menu #menu="matMenu">
      @for (link of group().links; track link.route) {
      <a mat-menu-item [routerLink]="link.route" routerLinkActive="!font-semibold" [routerLinkActiveOptions]="{ exact: link.exact ?? false }">
        {{ link.label | transloco }}
      </a>
      }
    </mat-menu>
  `,
})
export class NavGroupMenu {
  readonly group = input.required<PortalNavGroup>();
}

/**
 * Every page in one menu, for a toolbar too narrow to hold them side by side: a group's pages sit
 * under its name, so the menu reads the same way as the wide toolbar's drop-downs. A page of its
 * own that follows a group gets a rule above it, so it does not read as the group's last page.
 */
@Component({
  selector: 'upb-nav-collapsed-menu',
  imports: [RouterLink, RouterLinkActive, MatButtonModule, MatMenuModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button mat-button type="button" [matMenuTriggerFor]="menu">{{ 'nav.menu' | transloco }}</button>
    <mat-menu #menu="matMenu">
      @for (entry of entries(); track entry.label; let i = $index) {
      @if (ownSection(i)) {
      <div class="my-1 border-t border-border" role="separator"></div>
      }
      @if (entry.kind === 'group') {
      <div role="group" [attr.aria-label]="entry.label | transloco">
        <p class="px-4 pb-1 pt-3 text-xs font-medium text-ink-muted" aria-hidden="true">{{ entry.label | transloco }}</p>
        @for (link of entry.links; track link.route) {
        <a mat-menu-item [routerLink]="link.route" routerLinkActive="!font-semibold" [routerLinkActiveOptions]="{ exact: link.exact ?? false }">
          {{ link.label | transloco }}
        </a>
        }
      </div>
      } @else {
      <a mat-menu-item [routerLink]="entry.route" routerLinkActive="!font-semibold" [routerLinkActiveOptions]="{ exact: entry.exact ?? false }">
        {{ entry.label | transloco }}
      </a>
      }
      }
    </mat-menu>
  `,
})
export class NavCollapsedMenu {
  readonly entries = input.required<readonly PortalNavEntry[]>();

  protected ownSection(index: number): boolean {
    return startsOwnSection(this.entries(), index);
  }
}
