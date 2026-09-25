import { Injectable, inject, signal } from '@angular/core';
import { Api, DeliveryChargeDto, apiV1OrdersDeliveryChargeGet } from '@upbazaar/data-access';

/**
 * The delivery charge rule - a flat charge per order, free from a set value of goods - so the
 * cart and checkout can show what the buyer will pay before they place the order.
 *
 * Only for showing: checkout works the charge out again on the server, from the same rule, and the
 * order it returns is what the buyer is actually asked to pay.
 */
@Injectable({ providedIn: 'root' })
export class DeliveryCharge {
  private readonly api = inject(Api);
  private loading: Promise<void> | null = null;

  /** The rule, once loaded; null before then or if it could not be. */
  readonly rule = signal<DeliveryChargeDto | null>(null);

  /** Loads the rule once. A failure is quiet and is retried on the next call. */
  load(): Promise<void> {
    this.loading ??= this.api
      .invoke(apiV1OrdersDeliveryChargeGet, {})
      .then((rule) => this.rule.set(rule))
      .catch(() => {
        this.loading = null;
      });

    return this.loading;
  }

  /** The charge on goods worth <code>subtotal</code>, or null while the rule is unknown. */
  feeFor(subtotal: number): number | null {
    const rule = this.rule();

    if (!rule) {
      return null;
    }

    return rule.freeFrom !== null && subtotal >= rule.freeFrom ? 0 : rule.fee;
  }

  /** How much more the buyer needs to add for free delivery, or null when it is free already or never is. */
  shortOfFree(subtotal: number): number | null {
    const rule = this.rule();

    if (!rule || rule.fee === 0 || rule.freeFrom === null || subtotal >= rule.freeFrom) {
      return null;
    }

    return Math.round((rule.freeFrom - subtotal) * 100) / 100;
  }
}
