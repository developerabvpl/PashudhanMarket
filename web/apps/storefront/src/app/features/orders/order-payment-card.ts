import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { OrderDto, hasDelivery, orderRefund } from '@upbazaar/data-access';
import { InrCurrencyPipe } from '@upbazaar/util';
import { unpaidCancellation } from './order-amount';

/**
 * The order's money: how it is paid and what it comes to.
 *
 * The total counts only the goods the buyer keeps, so a cancelled order totals nothing. That is
 * right for the order but alarming for someone who paid online: they need to see what they paid
 * and what is coming back. So once anything paid online is being refunded, the card says both
 * beneath the total. A cash-on-delivery order has no online payment, but the buyer did pay - at the
 * door - and goods they returned are refunded to their UPI id: the card says what was paid on
 * delivery and what comes back in the same two rows (see orderRefund).
 *
 * Delivery reads "Free" only while something is being, or was, delivered. A cancelled order's
 * charge is zero because it was given back; calling that "Free" beside a refund of the ₹49 that
 * was paid for it would be wrong, so the row is left out and the refund rows carry the story.
 *
 * An order cancelled before any money changed hands - an online order whose payment never came, a
 * cash order that never reached the door - has no sums worth showing: "Subtotal ₹0, Total ₹0" read
 * as an order that cost nothing, while its row in My orders said "Not paid". The card says the same
 * as the row instead, and why (see unpaidCancellation).
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
      @if (unpaid(); as why) {
      <p class="mt-3 font-medium text-ink">{{ (why === 'notPaid' ? 'orders.notPaid' : 'orders.nothingToPay') | transloco }}</p>
      <p class="mt-1 text-ink-muted">{{ (why === 'notPaid' ? 'orders.notPaidNote' : 'orders.nothingToPayNote') | transloco }}</p>
      } @else {
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
        @if (o.shippingFee > 0 || delivering()) {
        <div class="flex justify-between">
          <dt class="text-ink-muted">{{ 'checkout.delivery' | transloco }}</dt>
          <dd class="text-ink">
            @if (o.shippingFee > 0) { {{ o.shippingFee | inr: 'symbol' : 'auto' }} } @else {
            {{ 'checkout.free' | transloco }} }
          </dd>
        </div>
        }
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
        @if (refund(); as r) {
        <!-- What was paid - online, or in cash at the door - then what of it comes back. -->
        @if (r.paid !== null) {
        <div class="flex justify-between pt-1.5">
          <dt class="text-ink-muted">{{ (r.to === 'upi' ? 'orders.paidOnDelivery' : 'orders.paidOnline') | transloco }}</dt>
          <dd class="text-ink">{{ r.paid | inr: 'symbol' : 'auto' }}</dd>
        </div>
        }
        <div [class]="r.paid === null ? 'flex justify-between pt-1.5' : 'flex justify-between'">
          <dt class="text-ink-muted">{{ (r.to === 'upi' ? 'orders.refundToUpi' : 'orders.refund') | transloco }}</dt>
          <dd class="font-medium text-success">{{ r.amount | inr: 'symbol' : 'auto' }}</dd>
        </div>
        }
      </dl>
      @if (refund(); as r) {
      <p class="mt-2 text-xs text-ink-muted">{{ (r.to === 'upi' ? 'orders.refundUpiNote' : 'orders.refundNote') | transloco }}</p>
      }
      }
    </div>
    }
  `,
})
export class OrderPaymentCard {
  readonly order = input.required<OrderDto>();

  /** What is going back: some of an online payment, or cash-on-delivery returns to the buyer's UPI id. */
  protected readonly refund = computed(() => orderRefund(this.order()));

  /** Something is, or was, on its way to the buyer - so a delivery charge of nothing means free delivery. */
  protected readonly delivering = computed(() => hasDelivery(this.order()));

  /** Cancelled before any money changed hands: the card says so in words rather than in zeroes. */
  protected readonly unpaid = computed(() => unpaidCancellation(this.order()));
}
