import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, OrderDto } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { OrderLookup } from './order-lookup';

const order: OrderDto = {
  id: 'o1',
  orderNumber: 'UPB-20260314-ABCD1234',
  customerId: 'c1',
  status: 'AwaitingPayment',
  subtotal: 2000,
  shippingFee: 49,
  total: 2049,
  currency: 'INR',
  placedAtUtc: '2026-03-14T10:00:00Z',
  lines: [
    {
      productId: 'p1',
      sku: 'UPB-SAREE-001',
      name: 'Banarasi Silk Saree',
      unitPrice: 1000,
      quantity: 2,
      lineTotal: 2000,
    },
  ],
  paymentId: 'pay-1',
  gatewayOrderId: 'order_test',
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
    invoke.mockResolvedValue(order);
    const fixture = await render();

    submitLookup(fixture.nativeElement, 'o1');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('UPB-20260314-ABCD1234');
    expect(fixture.nativeElement.textContent).toContain('UPB-SAREE-001');
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

    invoke.mockResolvedValue(order);
    retry.click();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('UPB-20260314-ABCD1234');
    expect(invoke).toHaveBeenCalledTimes(2);
  });
});
