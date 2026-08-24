import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import { DateIstPipe } from '@upbazaar/util';
import { SeoService } from '../../core/seo.service';

/**
 * The buyer's own account.
 *
 * Everything shown comes from the profile the guard already loaded, so the page needs no
 * fetch of its own and cannot flash empty.
 */
@Component({
  selector: 'upb-account-page',
  imports: [TranslocoPipe, DateIstPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (user(); as profile) {
    <section class="mx-auto max-w-2xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'account.title' | transloco }}</h1>

      <div class="upb-card mt-6 p-6">
        <h2 class="font-medium text-ink">{{ 'account.profile' | transloco }}</h2>

        <dl class="mt-4 grid grid-cols-3 gap-3 text-sm">
          <dt class="text-ink-muted">{{ 'account.displayName' | transloco }}</dt>
          <dd class="col-span-2 text-ink">{{ profile.displayName }}</dd>

          @if (profile.mobile) {
          <dt class="text-ink-muted">{{ 'account.mobile' | transloco }}</dt>
          <dd class="col-span-2 text-ink">
            +91 {{ profile.mobile }}
            <span class="ml-1 text-xs" [class.text-success]="profile.mobileVerified">
              ({{
                (profile.mobileVerified ? 'account.verified' : 'account.unverified') | transloco
              }})
            </span>
          </dd>
          } @if (profile.email) {
          <dt class="text-ink-muted">{{ 'account.email' | transloco }}</dt>
          <dd class="col-span-2 text-ink">
            {{ profile.email }}
            <span class="ml-1 text-xs" [class.text-success]="profile.emailVerified">
              ({{ (profile.emailVerified ? 'account.verified' : 'account.unverified') | transloco }})
            </span>
          </dd>
          }

          <dt class="text-ink-muted">{{ 'account.roles' | transloco }}</dt>
          <dd class="col-span-2 text-ink">
            {{ profile.roles.length > 0 ? profile.roles.join(', ') : ('staff.noRoles' | transloco) }}
          </dd>
        </dl>

        <p class="mt-6 text-sm text-ink-muted">
          {{ 'account.memberSince' | transloco: { date: profile.createdAtUtc | dateIst } }}
        </p>
      </div>
    </section>
    }
  `,
})
export class AccountPage {
  private readonly seo = inject(SeoService);

  protected readonly user = inject(CurrentUserStore).user;

  constructor() {
    // Account pages must never be indexed, whatever a crawler stumbles across.
    this.seo.apply({
      title: 'My account',
      description: 'Your UP Bazaar account.',
      canonicalPath: '/account',
      noIndex: true,
    });

    this.seo.setJsonLd(null);
  }
}
