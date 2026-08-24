import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthPanel } from './auth-panel';

/**
 * Full-page sign-in, for deep links and for guards that redirect here with a return URL.
 * The bottom sheet is the usual entry point; both render the same panel.
 */
@Component({
  selector: 'upb-sign-in-page',
  imports: [AuthPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-md px-4 py-10">
      <div class="upb-card p-6">
        <upb-auth-panel (signedIn)="complete()" />
      </div>
    </section>
  `,
})
export class SignInPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  complete(): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/products';

    void this.router.navigateByUrl(returnUrl);
  }
}
