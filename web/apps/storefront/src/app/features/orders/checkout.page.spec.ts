import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { HttpContext, HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import { Api, CALLER_SHOWS_ERRORS, apiV1OrdersDeliveryStatesGet, apiV1OrdersPost, apiV1PromotionsCouponsPreviewPost } from '@upbazaar/data-access';
import { ToastService, provideI18n } from '@upbazaar/ui';
import { DeliveryCharge } from '../../core/delivery-charge';
import { SeoService } from '../../core/seo.service';
import { translations } from '../../i18n/translations';
import { CartStore } from '../cart/cart.store';
import { OrderPayment } from '../payments/order-payment';
import { CheckoutPage } from './checkout.page';
import { LastAddress } from './last-address';

const ADDRESS = {
  fullName: 'Asha Devi',
  mobile: '9876543210',
  line1: '12 Gaushala Road',
  line2: null,
  landmark: null,
  city: 'Lucknow',
  district: null,
  state: 'Uttar Pradesh',
  pincode: '226024',
};

const COUPON = { code: 'WELCOME10', description: 'Welcome', discount: 50, deliveryDiscount: 0 };

/** The order endpoint fails with this; everything else the page asks for answers normally. */
async function render(placement: { status: number; error: Record<string, unknown> }) {
  const invoke = vi.fn(async (fn: unknown) => {
    if (fn === apiV1OrdersDeliveryStatesGet) {
      return ['Uttar Pradesh'];
    }

    if (fn === apiV1PromotionsCouponsPreviewPost) {
      return COUPON;
    }

    if (fn === apiV1OrdersPost) {
      throw new HttpErrorResponse(placement);
    }

    throw new Error('unexpected call');
  });

  const refresh = vi.fn(async () => undefined);

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(translations),
      provideRouter([]),
      { provide: Api, useValue: { invoke } },
      {
        provide: CartStore,
        useValue: {
          isLoading: signal(false),
          isEmpty: signal(false),
          canCheckOut: signal(true),
          subtotal: signal(600),
          lines: signal([{ productId: 'p1', sku: 'GHEE-1', name: 'Desi ghee', price: 600, priceWhenAdded: 600, currency: 'INR', quantity: 1, problem: null }]),
          refresh,
        },
      },
      { provide: DeliveryCharge, useValue: { load: async () => undefined, feeFor: () => 0 } },
      { provide: OrderPayment, useValue: { isOnlineEnabled: async () => false } },
      { provide: LastAddress, useValue: { read: () => ADDRESS, write: vi.fn() } },
      { provide: SeoService, useValue: { apply: vi.fn() } },
      { provide: CurrentUserStore, useValue: { user: signal({ id: 'u1', displayName: 'Asha Devi', mobile: '9876543210' }) } },
    ],
  });

  const fixture = TestBed.createComponent(CheckoutPage);
  await fixture.whenStable();

  const element = fixture.nativeElement as HTMLElement;
  const toasts = TestBed.inject(ToastService);

  return { fixture, element, invoke, refresh, toasts };
}

async function applyCoupon(fixture: { whenStable(): Promise<unknown> }, element: HTMLElement): Promise<void> {
  const input = element.querySelector('#coupon-code') as HTMLInputElement;
  input.value = COUPON.code;
  input.dispatchEvent(new Event('input'));
  await fixture.whenStable();

  [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Apply')!.click();
  await fixture.whenStable();
}

async function placeOrder(fixture: { whenStable(): Promise<unknown> }, element: HTMLElement): Promise<void> {
  element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  await fixture.whenStable();
}

function placementContext(invoke: ReturnType<typeof vi.fn>): HttpContext {
  const call = invoke.mock.calls.find((c) => c[0] === apiV1OrdersPost)!;

  return call[2] as HttpContext;
}

describe('CheckoutPage placing an order', () => {
  it('explains a coupon refused only at placement under the coupon box, once, with no toast', async () => {
    const { fixture, element, invoke, toasts } = await render({
      status: 400,
      error: { title: 'That coupon needs at least Rs 999 of the goods it covers.', code: 'promotions.coupon.below_minimum' },
    });

    await applyCoupon(fixture, element);
    expect(element.textContent).toContain('WELCOME10');

    await placeOrder(fixture, element);

    expect(placementContext(invoke).get(CALLER_SHOWS_ERRORS)).toBe(true);
    expect(toasts.toasts()).toEqual([]);

    const alerts = element.querySelectorAll('[role=alert]');
    expect(alerts.length).toBe(1);
    expect(alerts[0].textContent).toContain('needs at least ₹999');
    expect(element.textContent).not.toContain('Rs 999');
    expect(element.textContent).not.toContain('promotions.coupon');

    // The coupon is off the order, its code left in the box to fix or clear.
    expect((element.querySelector('#coupon-code') as HTMLInputElement).value).toBe('WELCOME10');
    expect(element.textContent).not.toContain('You save');
  });

  it('treats Orders refusing a free-delivery coupon as a coupon problem too', async () => {
    const { fixture, element, toasts } = await render({
      status: 400,
      error: { title: 'Delivery is already free on this order, so that coupon takes nothing off.', code: 'orders.coupon.delivery_already_free' },
    });

    await applyCoupon(fixture, element);
    await placeOrder(fixture, element);

    expect(toasts.toasts()).toEqual([]);
    expect(element.querySelector('#coupon-code-error')?.textContent).toContain('Delivery is already free');
  });

  it('still reports stock that sold out, in one translated toast, and refreshes the basket', async () => {
    const { fixture, element, refresh, toasts } = await render({
      status: 409,
      error: { title: 'Some items sold out while you were checking out. Review your cart and try again.', code: 'orders.out_of_stock' },
    });

    await placeOrder(fixture, element);

    const shown = toasts.toasts();
    expect(shown.length).toBe(1);
    expect(shown[0]).toMatchObject({ tone: 'error', message: 'checkout.errors.outOfStock', detail: 'orders.out_of_stock' });
    expect(TestBed.inject(TranslocoService).translate(shown[0].message)).toContain('sold out');
    expect(refresh).toHaveBeenCalled();
    expect(element.querySelectorAll('[role=alert]').length).toBe(0);
  });

  it('shows the API title for a failure it has no words of its own for', async () => {
    const { fixture, element, toasts } = await render({
      status: 409,
      error: { title: 'Everything in one order must be priced in the same currency.', code: 'orders.mixed_currencies' },
    });

    await placeOrder(fixture, element);

    expect(toasts.toasts()).toEqual([
      expect.objectContaining({ message: 'Everything in one order must be priced in the same currency.', detail: 'orders.mixed_currencies' }),
    ]);
  });

  it('puts an address the API rejects under its input, without a toast', async () => {
    const { fixture, element, toasts } = await render({
      status: 400,
      error: { title: 'One or more validation errors occurred.', code: 'validation.failed', errors: { 'Address.Pincode': ['validation.pincode'] } },
    });

    await placeOrder(fixture, element);

    expect(toasts.toasts()).toEqual([]);
    expect(element.querySelector('#pincode')?.getAttribute('aria-invalid')).toBe('true');
  });
});
