import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { missingPermissionForDevelopers } from '../pages/forbidden.page';

/**
 * Where permissionGuard sends a signed-in portal user who lacks the permission.
 *
 * The portals' own page rather than the storefront's: its button is a Material one in the portal's
 * theme, where the shared page's storefront-styled button stood out orange in a blue toolbar's
 * app. It lives with the other Material pages so the storefront never downloads Material for it.
 */
@Component({
  selector: 'upb-portal-forbidden-page',
  imports: [TranslocoPipe, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-md px-4 py-12 text-center">
      <h1 class="text-2xl font-semibold text-ink">{{ 'forbidden.title' | transloco }}</h1>
      <p class="mt-3 text-ink-muted">{{ 'forbidden.body' | transloco }}</p>

      @if (required) {
      <p class="mt-4 font-mono text-xs text-ink-muted">{{ required }}</p>
      }

      <button mat-flat-button color="primary" type="button" class="mt-8" (click)="back()">
        {{ 'forbidden.back' | transloco }}
      </button>
    </section>
  `,
})
export class PortalForbiddenPage {
  private readonly location = inject(Location);

  protected readonly required = missingPermissionForDevelopers(inject(ActivatedRoute));

  back(): void {
    this.location.back();
  }
}
