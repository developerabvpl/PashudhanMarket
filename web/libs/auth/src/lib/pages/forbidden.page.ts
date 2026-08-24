import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Where permissionGuard sends a signed-in user who lacks the permission. Naming the missing
 * permission turns "access denied" into something an admin can actually act on.
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
      <p class="mt-4 font-mono text-sm text-ink-muted">
        {{ 'forbidden.required' | transloco: { permissions: required } }}
      </p>
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

  protected readonly required = inject(ActivatedRoute).snapshot.queryParamMap.get('required');

  back(): void {
    this.location.back();
  }
}
