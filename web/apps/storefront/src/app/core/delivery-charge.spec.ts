import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api } from '@upbazaar/data-access';
import { DeliveryCharge } from './delivery-charge';

function service(invoke: ReturnType<typeof vi.fn>): DeliveryCharge {
  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), { provide: Api, useValue: { invoke } }],
  });

  return TestBed.inject(DeliveryCharge);
}

describe('DeliveryCharge', () => {
  it('charges below the free-delivery value and says how far off it is', async () => {
    const delivery = service(vi.fn(async () => ({ fee: 49, freeFrom: 499, currency: 'INR' })));
    await delivery.load();

    expect(delivery.feeFor(150)).toBe(49);
    expect(delivery.shortOfFree(150.5)).toBe(348.5);
    expect(delivery.feeFor(499)).toBe(0);
    expect(delivery.shortOfFree(499)).toBeNull();
  });

  it('never promises free delivery when there is no such value', async () => {
    const delivery = service(vi.fn(async () => ({ fee: 49, freeFrom: null, currency: 'INR' })));
    await delivery.load();

    expect(delivery.feeFor(5000)).toBe(49);
    expect(delivery.shortOfFree(5000)).toBeNull();
  });

  it('knows nothing until the rule has loaded, and asks for it only once', async () => {
    const invoke = vi.fn(async () => ({ fee: 49, freeFrom: 499, currency: 'INR' }));
    const delivery = service(invoke);

    expect(delivery.feeFor(150)).toBeNull();

    await Promise.all([delivery.load(), delivery.load()]);
    expect(invoke).toHaveBeenCalledTimes(1);
  });

  it('tries again after a failure', async () => {
    const invoke = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValue({ fee: 49, freeFrom: 499, currency: 'INR' });
    const delivery = service(invoke);

    await delivery.load();
    expect(delivery.feeFor(150)).toBeNull();

    await delivery.load();
    expect(delivery.feeFor(150)).toBe(49);
  });
});
