import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { OrderDto } from '@upbazaar/data-access';
import { InrCurrencyPipe } from '@upbazaar/util';

/**
 * The order's money: how it is paid and what it comes to.
 *
 * The total counts only the goods the buyer keeps, so a cancelled order totals nothing. That is
 * right for the order but alarming for someone who paid online: they need to see what they paid
 * and what is coming back. So once anything paid online is being refunded, the card says both
 * beneath the total.
 */
@Component({
  selector: 'upb-order-payment-card',
  imports: [TranslocoPipe, InrCurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (order(); as o) {
    <div class="rounded-card border border-border bg-surface p-4 text-sm">
      <h2 class="font-medium text-ink">{{ 'orders.payment' | transloco }}</h2>
      <p class="mt-2 text-ink-muted">{{ 'orders.paymentMethod.' + o.paymentMethod | transloco }}</p>
      <dl class="mt-3 space-y-1.5">
        <div class="flex justify-between">
          <dt class="text-ink-muted">{{ 'cart.subtotal' | transloco }}</dt>
          <dd class="text-ink">{{ o.subtotal | inr: 'symbol' : 'auto' }}</dd>
        </div>
        @if (o.discount > 0) {
        <div class="flex justify-between">
          <dt class="text-ink-muted">{{ 'checkout.coupon.discount' | transloco: { code: o.couponCode ?? '' } }}</dt>
          <dd class="text-success">− {{ o.discount | inr: 'symbol' : 'auto' }}</dd>
        </div>
        }
        <div class="flex justify-between">
          <dt class="text-ink-muted">{{ 'checkout.delivery' | transloco }}</dt>
          <dd class="text-ink">
            @if (o.shippingFee > 0) { {{ o.shippingFee | inr: 'symbol' : 'auto' }} } @else {
            {{ 'checkout.free' | transloco }} }
          </dd>
        </div>
        @if (o.deliveryDiscount > 0) {
        <div class="flex justify-between">
          <dt class="text-ink-muted">{{ 'checkout.coupon.freeDelivery' | transloco: { code: o.couponCode ?? '' } }}</dt>
          <dd class="text-success">− {{ o.deliveryDiscount | inr: 'symbol' : 'auto' }}</dd>
        </div>
        }
        <div class="flex justify-between border-t border-border pt-1.5 font-bold">
          <dt class="text-ink">{{ 'checkout.total' | transloco }}</dt>
          <dd class="text-ink">{{ o.total | inr: 'symbol' : 'auto' }}</dd>
        </div>
        @if (refunding()) {
        <div class="flex justify-between pt-1.5">
          <dt class="text-ink-muted">{{ 'orders.paidOnline' | transloco }}</dt>
          <dd class="text-ink">{{ o.amountPaid | inr: 'symbol' : 'auto' }}</dd>
        </div>
        <div class="flex justify-between">
          <dt class="text-ink-muted">{{ 'orders.refund' | transloco }}</dt>
          <dd class="font-medium text-success">{{ o.refundTotal | inr: 'symbol' : 'auto' }}</dd>
        </div>
        }
      </dl>
      @if (refunding()) {
      <p class="mt-2 text-xs text-ink-muted">{{ 'orders.refundNote' | transloco }}</p>
      }
    </div>
    }
  `,
})
export class OrderPaymentCard {
  readonly order = input.required<OrderDto>();

  /** Paid online, and some or all of it is going back. */
  protected readonly refunding = computed(() => {
    const o = this.order();

    return o.amountPaid !== null && o.amountPaid !== undefined && o.refundTotal > 0;
  });
}
