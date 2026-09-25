import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import { Api, ShipmentDto, apiV1AdminShippingShipmentsShipmentIdChargesTripPut } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { ShipmentCharges } from './shipment-charges';

function shipment(overrides: Partial<ShipmentDto> = {}): ShipmentDto {
  return {
    id: 'sh1',
    orderId: 'o1',
    orderNumber: 'UPB-260925-ABCDEF',
    orderPartId: 'p1',
    sellerId: 's1',
    status: 'InTransit',
    direction: 'Forward',
    carrier: 'Fake',
    pickupLocation: 'Warehouse',
    parcel: { weightGrams: 300, lengthCm: 20, breadthCm: 15, heightCm: 10 },
    paymentMode: 'COD',
    codAmount: 150,
    carrierOrderId: '9123',
    awb: 'FAKE123',
    courierName: 'Fake Express',
    trackingUrl: null,
    lastError: null,
    events: [],
    createdAtUtc: '2026-09-25T10:00:00Z',
    quoteError: null,
    charges: [{ trip: 'Delivery', amount: 70, charged: 70, incurredAtUtc: '2026-09-25T11:00:00Z', correctedAtUtc: null, note: null }],
    ...overrides,
  };
}

async function render(value: ShipmentDto, invoke = vi.fn()) {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(),
      { provide: Api, useValue: { invoke } },
      { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
    ],
  });

  const fixture = TestBed.createComponent(ShipmentCharges);
  fixture.componentRef.setInput('shipment', value);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

describe('ShipmentCharges', () => {
  it('shows what each trip costs and what has been charged', async () => {
    const { element } = await render(shipment());

    expect(element.textContent).toContain('Delivery:');
    expect(element.textContent).toContain('₹70');
    expect(element.textContent).toContain('charged');
  });

  it('says why a parcel was not priced', async () => {
    const { element } = await render(
      shipment({
        quoteError: 'The pickup location has no PIN code.',
        charges: [{ trip: 'Delivery', amount: null, charged: 0, incurredAtUtc: null, correctedAtUtc: null, note: null }],
      })
    );

    expect(element.textContent).toContain('The pickup location has no PIN code.');
    expect(element.textContent).toContain('not known yet');
  });

  it('corrects a charge from the invoice', async () => {
    const corrected = shipment();
    const { fixture, invoke, element } = await render(shipment(), vi.fn(async () => corrected));
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);

    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Correct')!.click();
    await fixture.whenStable();

    const amount = element.querySelector('input[name="amount"]') as HTMLInputElement;
    amount.value = '85';
    amount.dispatchEvent(new Event('input'));
    const note = element.querySelector('input[name="note"]') as HTMLInputElement;
    note.value = ' Invoice 1234 ';
    note.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminShippingShipmentsShipmentIdChargesTripPut, {
      shipmentId: 'sh1',
      trip: 'Delivery',
      body: { amount: 85, note: 'Invoice 1234' },
    });
    expect(changed).toHaveBeenCalledWith(corrected);
  });
});
