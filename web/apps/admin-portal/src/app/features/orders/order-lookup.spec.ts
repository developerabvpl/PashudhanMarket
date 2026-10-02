import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, OrderDto, apiV1AdminOrdersGet, apiV1AdminShippingOrdersOrderIdShipmentsGet } from '@upbazaar/data-access';
import { TranslocoService } from '@jsverse/transloco';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { translations } from '../../i18n/translations';
import { OrderLookup } from './order-lookup';

const ORDER_ID = '3f2b8c1e-5d4a-4b6f-9e2d-7a1c0b9d8e6f';

const order: OrderDto = {
  id: ORDER_ID,
  number: 'UPB-260314-ABCD12',
  buyerId: 'b1',
  status: 'Confirmed',
  paymentMethod: 'Online',
  paymentStatus: 'Paid',
  subtotal: 2000,
  discount: 0,
  shippingFee: 0,
  deliveryDiscount: 0,
  couponCode: null,
  total: 2000,
  currency: 'INR',
  deliveryAddress: {
    fullName: 'Asha Devi',
    mobile: '9876543210',
    line1: '12 Gaushala Road',
    line2: null,
    landmark: null,
    city: 'Lucknow',
    district: null,
    state: 'Uttar Pradesh',
    pincode: '226001',
  },
  parts: [
    {
      id: 'part1',
      sellerId: 's1',
      status: 'Confirmed',
      subtotal: 2000,
      discount: 0,
      cancellationReason: null,
      returnCondition: null,
      deliveredAtUtc: null,
      returnableUntilUtc: null,
      returnRequest: null,
      lines: [
        {
          productId: 'p1',
          sku: 'UPB-DIYA-001',
          name: 'Gobar Diya, pack of 12',
          unitPrice: 1000,
          quantity: 2,
          lineTotal: 2000,
        },
      ],
    },
  ],
  placedAtUtc: '2026-03-14T10:00:00Z',
  paymentDueAtUtc: null,
  paymentReference: 'pay-1',
  cancelledAtUtc: null,
  cancellationReason: null,
  canCancel: true,
  amountPaid: 2000,
  refundTotal: 0,
};

/** Submits the lookup form the way a user would, so the component's own handler runs. */
function submitLookup(element: HTMLElement, orderId: string): void {
  const input = element.querySelector<HTMLInputElement>('input[name="orderId"]');
  const form = element.querySelector('form');

  input!.value = orderId;
  form!.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
}

describe('OrderLookup', () => {
  let invoke: ReturnType<typeof vi.fn>;

  /** The lookup answers with the order; the parcels section beneath it asks for shipments, of which there are none. */
  function answerWith(found: OrderDto): void {
    invoke.mockImplementation(async (fn: unknown) => (fn === apiV1AdminShippingOrdersOrderIdShipmentsGet ? [] : found));
  }

  beforeEach(() => {
    invoke = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n(translations),
        { provide: Api, useValue: { invoke } },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(OrderLookup);
    await fixture.whenStable();

    return fixture;
  }

  it('starts idle with no result on screen', async () => {
    const fixture = await render();

    expect(fixture.nativeElement.querySelector('article')).toBeNull();
    expect(invoke).not.toHaveBeenCalled();
  });

  it('shows the order once it loads', async () => {
    answerWith(order);
    const fixture = await render();

    submitLookup(fixture.nativeElement, ORDER_ID);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('UPB-260314-ABCD12');
    expect(fixture.nativeElement.textContent).toContain('UPB-DIYA-001');
  });

  it('shows the coupon discount so the full-price parcels add up to the total', async () => {
    const part = order.parts[0];
    answerWith({
      ...order,
      paymentMethod: 'CashOnDelivery',
      paymentStatus: 'CashOnDelivery',
      paymentReference: null,
      amountPaid: null,
      subtotal: 597,
      discount: 59.7,
      total: 537.3,
      couponCode: 'WELCOME10',
      parts: [{ ...part, subtotal: 597, discount: 59.7 }],
    });
    const fixture = await render();

    submitLookup(fixture.nativeElement, ORDER_ID);
    await fixture.whenStable();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    const minus = String.fromCharCode(0x2212);
    expect(text).toContain(`Coupon WELCOME10${minus}₹59.70`);
    expect(text).toContain('Total₹537.30');
    expect(text).toContain(`₹597.00 · ${minus}₹59.70 coupon`);

    // Cash on delivery has no payment reference, and says how it is paid instead of a dash.
    expect(text).toContain('Payment Cash on delivery');
  });

  it('shows what a cancelled online order was paid and what goes back', async () => {
    answerWith({ ...order, status: 'Cancelled', subtotal: 0, total: 0, amountPaid: 2000, refundTotal: 2000 });
    const fixture = await render();

    submitLookup(fixture.nativeElement, ORDER_ID);
    await fixture.whenStable();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    expect(text).toContain('Online payment pay-1');
    expect(text).toContain('Paid online₹2,000.00');
    expect(text).toContain('Refund₹2,000.00');
  });

  it('labels the order status in the page language, not the raw API value', async () => {
    answerWith({ ...order, status: 'Completed' });
    const fixture = await render();
    const transloco = TestBed.inject(TranslocoService);

    submitLookup(fixture.nativeElement, ORDER_ID);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Status: Delivered');

    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('पहुँचा दिया');
    expect(text).not.toContain('Completed');
  });

  it('does not call the API for an empty id', async () => {
    const fixture = await render();

    submitLookup(fixture.nativeElement, '   ');
    await fixture.whenStable();

    expect(invoke).not.toHaveBeenCalled();
  });

  it('treats a 404 as "no such order" rather than a failure', async () => {
    invoke.mockRejectedValue(new HttpErrorResponse({ status: 404, error: {} }));
    const fixture = await render();

    submitLookup(fixture.nativeElement, '00000000-0000-0000-0000-000000000000');
    await fixture.whenStable();

    // The empty state renders; the error state with its retry button does not.
    expect(fixture.nativeElement.querySelector('button[type="button"]')).toBeNull();
  });

  it('offers a retry when the lookup genuinely fails', async () => {
    invoke.mockRejectedValue(new HttpErrorResponse({ status: 500, error: {} }));
    const fixture = await render();

    submitLookup(fixture.nativeElement, ORDER_ID);
    await fixture.whenStable();

    const retry = fixture.nativeElement.querySelector('button[type="button"]') as HTMLButtonElement;
    expect(retry).not.toBeNull();

    answerWith(order);
    retry.click();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('UPB-260314-ABCD12');
    expect(invoke.mock.calls.filter(([fn]) => fn !== apiV1AdminShippingOrdersOrderIdShipmentsGet)).toHaveLength(2);
  });
  /** The orders search answers with these summaries; opening one answers with the order. */
  function searchFinds(numbers: readonly string[]): void {
    const items = numbers.map((number, i) => ({
      id: i === 0 ? ORDER_ID : `id-${i}`,
      number,
      status: 'Confirmed',
      paymentMethod: 'Online',
      paymentStatus: 'Paid',
      total: 2000,
      currency: 'INR',
      itemCount: 2,
      placedAtUtc: '2026-03-14T10:00:00Z',
      partStatuses: ['Confirmed'],
    }));

    invoke.mockImplementation(async (fn: unknown) =>
      fn === apiV1AdminShippingOrdersOrderIdShipmentsGet ? [] : fn === apiV1AdminOrdersGet ? { items } : order
    );
  }

  it('finds an order by its number, as a buyer reads it out', async () => {
    searchFinds(['UPB-260314-ABCD12']);
    const fixture = await render();

    submitLookup(fixture.nativeElement, 'upb-260314-abcd12');
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminOrdersGet, { Number: 'upb-260314-abcd12', PageSize: 20 });
    expect(fixture.nativeElement.textContent).toContain('UPB-DIYA-001');
  });

  it('lists the orders to choose from when part of a number matches several', async () => {
    searchFinds(['UPB-260314-ABCD12', 'UPB-260314-ABCD99']);
    const fixture = await render();

    submitLookup(fixture.nativeElement, 'ABCD');
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('UPB-260314-ABCD99');
    expect(fixture.nativeElement.querySelector('article')).toBeNull();
  });

  it('says so when no order number matches', async () => {
    searchFinds([]);
    const fixture = await render();

    submitLookup(fixture.nativeElement, 'NOPE');
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('article')).toBeNull();
    expect(fixture.nativeElement.querySelector('button[type="button"]')).toBeNull();
  });

  it('shows a confirmed order whose parcel has left the seller as shipped', async () => {
    answerWith({ ...order, parts: [{ ...order.parts[0], status: 'Shipped' }] });
    const fixture = await render();

    submitLookup(fixture.nativeElement, ORDER_ID);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Status: Shipped');
  });
});
