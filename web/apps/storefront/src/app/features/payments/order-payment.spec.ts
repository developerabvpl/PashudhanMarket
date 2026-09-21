import { TestBed } from '@angular/core/testing';
import {
  Api,
  CheckoutSessionDto,
  apiV1PaymentsConfigGet,
  apiV1PaymentsFakeGatewayOrderIdPayPost,
  apiV1PaymentsOrdersOrderIdCheckoutPost,
  apiV1PaymentsRazorpayVerifyPost,
} from '@upbazaar/data-access';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderPayment } from './order-payment';
import { RazorpayCheckout, RazorpaySuccess } from './razorpay-checkout';

function session(gateway: string): CheckoutSessionDto {
  return {
    paymentId: 'pmt1',
    gateway,
    keyId: gateway === 'Razorpay' ? 'rzp_test_key' : '',
    gatewayOrderId: 'order_1',
    amountInPaise: 41700,
    currency: 'INR',
    orderNumber: 'UPB-260922-ABCDEF',
    expiresAtUtc: '2026-09-22T10:15:00Z',
  };
}

describe('OrderPayment', () => {
  let answers: Map<unknown, (params: unknown) => unknown>;
  let invoke: ReturnType<typeof vi.fn>;
  let checkoutResult: RazorpaySuccess | null;
  let payment: OrderPayment;

  beforeEach(() => {
    answers = new Map();
    invoke = vi.fn(async (fn: unknown, params: unknown) => {
      const answer = answers.get(fn);

      if (!answer) {
        throw new Error('unexpected call');
      }

      return answer(params);
    });
    checkoutResult = null;

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: Api, useValue: { invoke } },
        { provide: RazorpayCheckout, useValue: { open: vi.fn(async () => checkoutResult) } },
      ],
    });

    payment = TestBed.inject(OrderPayment);
  });

  it('reports online payment as off when the config cannot be read', async () => {
    expect(await payment.isOnlineEnabled()).toBe(false);
  });

  it('reads whether online payment is on', async () => {
    answers.set(apiV1PaymentsConfigGet, () => ({ onlineEnabled: true, gateway: 'Razorpay' }));

    expect(await payment.isOnlineEnabled()).toBe(true);
  });

  it('hands back to the page to simulate when the fake gateway is in use', async () => {
    answers.set(apiV1PaymentsOrdersOrderIdCheckoutPost, () => session('Fake'));

    const { outcome } = await payment.pay('o1', {});

    expect(outcome).toBe('simulate');
    expect(TestBed.inject(RazorpayCheckout).open).not.toHaveBeenCalled();
  });

  it('verifies what Checkout returns and reports the order confirmed', async () => {
    answers.set(apiV1PaymentsOrdersOrderIdCheckoutPost, () => session('Razorpay'));
    answers.set(apiV1PaymentsRazorpayVerifyPost, () => ({ orderId: 'o1', outcome: 'Confirmed' }));
    checkoutResult = { razorpay_order_id: 'order_1', razorpay_payment_id: 'pay_1', razorpay_signature: 'sig' };

    const { outcome } = await payment.pay('o1', { name: 'Asha', contact: '9876543210' });

    expect(outcome).toBe('confirmed');
    expect(invoke).toHaveBeenCalledWith(apiV1PaymentsRazorpayVerifyPost, {
      body: { gatewayOrderId: 'order_1', gatewayPaymentId: 'pay_1', signature: 'sig' },
    });
  });

  it('verifies nothing when the buyer closes the window', async () => {
    answers.set(apiV1PaymentsOrdersOrderIdCheckoutPost, () => session('Razorpay'));

    const { outcome } = await payment.pay('o1', {});

    expect(outcome).toBe('dismissed');
    expect(invoke).not.toHaveBeenCalledWith(apiV1PaymentsRazorpayVerifyPost, expect.anything());
  });

  it('reports a payment the order could not take as a refund', async () => {
    answers.set(apiV1PaymentsFakeGatewayOrderIdPayPost, () => ({ orderId: 'o1', outcome: 'RefundDue' }));

    expect(await payment.simulate(session('Fake'))).toBe('refund');
  });
});
