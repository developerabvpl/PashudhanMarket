import { ChangeDetectionStrategy, Component, inject, isDevMode } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * The permission a guard turned the visitor away for, for a developer only. A permission key such
 * as `settlements.own.read` means nothing to a seller's packer or a member of staff, so the page
 * says in plain words that the account may not open it and whom to ask; the key shows, small, only
 * in a development build, where it tells the developer which guard refused.
 */
export function missingPermissionForDevelopers(route: ActivatedRoute): string | null {
  return isDevMode() ? route.snapshot.queryParamMap.get('required') : null;
}

/**
 * Where permissionGuard sends a signed-in user who lacks the permission, on the storefront. The
 * portals show {@link PortalForbiddenPage} instead, which says the same with a Material button.
 */
@Component({
  selector: 'upb-forbidden-page',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-md px-4 py-12 text-center">
      <h1 class="text-2xl font-semibold text-ink">{{ 'forbidden.title' | transloco }}</h1>
      <p class="mt-3 text-ink-muted">{{ 'forbidden.body' | transloco }}</p>

      @if (required) {
      <p class="mt-4 font-mono text-xs text-ink-muted">{{ required }}</p>
      }

      <button
        type="button"
        class="mt-8 rounded-control bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700"
        (click)="back()"
      >
        {{ 'forbidden.back' | transloco }}
      </button>
    </section>
  `,
})
export class ForbiddenPage {
  private readonly location = inject(Location);

  protected readonly required = missingPermissionForDevelopers(inject(ActivatedRoute));

  back(): void {
    this.location.back();
  }
}
