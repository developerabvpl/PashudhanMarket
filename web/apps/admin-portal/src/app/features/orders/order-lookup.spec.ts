import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, OrderDto, apiV1AdminShippingOrdersOrderIdShipmentsGet } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { OrderLookup } from './order-lookup';

const order: OrderDto = {
  id: 'o1',
  number: 'UPB-260314-ABCD12',
  buyerId: 'b1',
  status: 'Confirmed',
  paymentMethod: 'Online',
  paymentStatus: 'Paid',
  subtotal: 2000,
  discount: 0,
  shippingFee: 0,
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
        provideI18n(),
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

    submitLookup(fixture.nativeElement, 'o1');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('UPB-260314-ABCD12');
    expect(fixture.nativeElement.textContent).toContain('UPB-DIYA-001');
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

    submitLookup(fixture.nativeElement, 'missing');
    await fixture.whenStable();

    // The empty state renders; the error state with its retry button does not.
    expect(fixture.nativeElement.querySelector('button[type="button"]')).toBeNull();
  });

  it('offers a retry when the lookup genuinely fails', async () => {
    invoke.mockRejectedValue(new HttpErrorResponse({ status: 500, error: {} }));
    const fixture = await render();

    submitLookup(fixture.nativeElement, 'o1');
    await fixture.whenStable();

    const retry = fixture.nativeElement.querySelector('button[type="button"]') as HTMLButtonElement;
    expect(retry).not.toBeNull();

    answerWith(order);
    retry.click();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('UPB-260314-ABCD12');
    expect(invoke.mock.calls.filter(([fn]) => fn !== apiV1AdminShippingOrdersOrderIdShipmentsGet)).toHaveLength(2);
  });
});
