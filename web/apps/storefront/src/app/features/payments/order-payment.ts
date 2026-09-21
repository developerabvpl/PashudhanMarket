import { Injectable, inject } from '@angular/core';
import {
  Api,
  CheckoutSessionDto,
  PaymentsConfigDto,
  apiV1PaymentsConfigGet,
  apiV1PaymentsFakeGatewayOrderIdPayPost,
  apiV1PaymentsOrdersOrderIdCheckoutPost,
  apiV1PaymentsRazorpayVerifyPost,
} from '@upbazaar/data-access';
import { PayerDetails, RazorpayCheckout } from './razorpay-checkout';

/**
 * How one attempt to pay ended, from the buyer's side.
 *
 * - `confirmed`: paid, and the order is confirmed.
 * - `processing`: paid; the order will be confirmed within a minute.
 * - `refund`: paid, but the order could no longer take it, so the money is being refunded.
 * - `dismissed`: the buyer closed the payment window.
 * - `simulate`: the fake gateway is in use; the page should ask whether to simulate a payment.
 */
export type PaymentOutcome = 'confirmed' | 'processing' | 'refund' | 'dismissed' | 'simulate';

/**
 * Paying for an order online, from "Pay now" to an outcome.
 *
 * The page asks the API for a checkout session, opens Razorpay Checkout with it, and hands what
 * Checkout returns back to the API to verify. Nothing about the payment is trusted from the
 * browser: the API checks Razorpay's signature before it confirms anything.
 *
 * In development the API runs a fake gateway instead of Razorpay. There is no Checkout window
 * then; the page offers to simulate a successful payment, which goes through the same
 * verification on the server.
 */
@Injectable({ providedIn: 'root' })
export class OrderPayment {
  private readonly api = inject(Api);
  private readonly razorpay = inject(RazorpayCheckout);

  private config: Promise<PaymentsConfigDto> | null = null;

  /** Whether online payment can be offered; asked once per page load. */
  async isOnlineEnabled(): Promise<boolean> {
    try {
      this.config ??= this.api.invoke(apiV1PaymentsConfigGet, {});

      return (await this.config).onlineEnabled;
    } catch {
      this.config = null;

      return false;
    }
  }

  /** Opens payment for an order. Resolves once the buyer has paid, given up, or must choose to simulate. */
  async pay(orderId: string, payer: PayerDetails): Promise<{ outcome: PaymentOutcome; session: CheckoutSessionDto }> {
    const session = await this.api.invoke(apiV1PaymentsOrdersOrderIdCheckoutPost, { orderId });

    if (session.gateway === 'Fake') {
      return { outcome: 'simulate', session };
    }

    const paid = await this.razorpay.open(session, payer);

    if (paid === null) {
      return { outcome: 'dismissed', session };
    }

    const result = await this.api.invoke(apiV1PaymentsRazorpayVerifyPost, {
      body: {
        gatewayOrderId: paid.razorpay_order_id,
        gatewayPaymentId: paid.razorpay_payment_id,
        signature: paid.razorpay_signature,
      },
    });

    return { outcome: toOutcome(result.outcome), session };
  }

  /** Development only: completes a fake-gateway payment as if Checkout had succeeded. */
  async simulate(session: CheckoutSessionDto): Promise<PaymentOutcome> {
    const result = await this.api.invoke(apiV1PaymentsFakeGatewayOrderIdPayPost, {
      gatewayOrderId: session.gatewayOrderId,
    });

    return toOutcome(result.outcome);
  }
}

function toOutcome(outcome: string): PaymentOutcome {
  switch (outcome) {
    case 'Confirmed':
      return 'confirmed';
    case 'RefundDue':
      return 'refund';
    default:
      return 'processing';
  }
}
