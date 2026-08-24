import {
  Directive,
  TemplateRef,
  ViewContainerRef,
  computed,
  effect,
  inject,
  input,
} from '@angular/core';
import { CurrentUserStore } from './current-user-store';

/**
 * Structural directive that renders its content only when the user holds the permission.
 *
 * ```html
 * <button *hasPermission="'catalog.products.write'">Add product</button>
 * <a *hasPermission="['payments.refunds.write', 'payments.payments.read']">Refund</a>
 * ```
 *
 * This hides affordances the user cannot use; it is not a security boundary. The API rejects
 * the call regardless, and permissionGuard covers whole routes.
 */
@Directive({ selector: '[hasPermission]' })
export class HasPermissionDirective {
  private readonly currentUser = inject(CurrentUserStore);
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);

  readonly hasPermission = input.required<string | readonly string[]>();

  /** Inverts the check, for "you need permission X to do this" placeholders. */
  readonly hasPermissionElse = input(false);

  private readonly granted = computed(() => {
    const required = this.hasPermission();
    const permissions = typeof required === 'string' ? [required] : required;
    const allowed = this.currentUser.hasAll(permissions);

    return this.hasPermissionElse() ? !allowed : allowed;
  });

  constructor() {
    effect(() => {
      const shouldRender = this.granted();
      const isRendered = this.viewContainer.length > 0;

      if (shouldRender && !isRendered) {
        this.viewContainer.createEmbeddedView(this.templateRef);
      } else if (!shouldRender && isRendered) {
        this.viewContainer.clear();
      }
    });
  }
}
