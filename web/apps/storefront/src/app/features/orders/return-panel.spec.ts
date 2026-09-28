import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, OrderDto, OrderPartDto, apiV1OrdersOrderIdPartsPartIdReturnPost } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { ReturnPanel } from './return-panel';

const inAWeek = new Date(Date.now() + 7 * 24 * 3600 * 1000).toISOString();

const part: OrderPartDto = {
  id: 'part1',
  sellerId: 's1',
  status: 'Delivered',
  subtotal: 150,
  discount: 0,
  lines: [{ productId: 'p1', sku: 'DIYA-12', name: 'Gobar Diya', unitPrice: 75, quantity: 2, lineTotal: 150, discount: 0 }],
  cancellationReason: null,
  returnCondition: null,
  deliveredAtUtc: new Date().toISOString(),
  returnableUntilUtc: inAWeek,
  returnRequest: null,
};

const order = {
  id: 'o1',
  number: 'UPB-260923-ABCDEF',
  paymentMethod: 'CashOnDelivery',
  parts: [part],
} as unknown as OrderDto;

function render(p: OrderPartDto, invoke = vi.fn()) {
  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(translations), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(ReturnPanel);
  fixture.componentRef.setInput('order', order);
  fixture.componentRef.setInput('part', p);

  return { fixture, invoke };
}

describe('ReturnPanel', () => {
  it('asks a cash buyer for a UPI id and sends the whole parcel back', async () => {
    const { fixture, invoke } = render(part, vi.fn(async () => order));
    await fixture.whenStable();

    const start = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: HTMLButtonElement) => b.textContent?.trim() === 'Return this parcel'
    ) as HTMLButtonElement;
    start.click();
    await fixture.whenStable();

    const upi = fixture.nativeElement.querySelector('input[placeholder="name@okicici"]') as HTMLInputElement;
    upi.value = ' asha@okicici ';
    upi.dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1OrdersOrderIdPartsPartIdReturnPost, {
      orderId: 'o1',
      partId: 'part1',
      body: { reason: 'Damaged', comment: null, refundUpiId: 'asha@okicici', items: [{ productId: 'p1', quantity: 2 }] },
    });
  });

  it('insists on a comment when the reason is something else', async () => {
    const { fixture, invoke } = render(part);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();

    const select = fixture.nativeElement.querySelector('select[id^="return-reason"]') as HTMLSelectElement;
    select.value = 'Other';
    select.dispatchEvent(new Event('change'));
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Tell the seller what is wrong.');
  });

  it('sends back only the units chosen, and says roughly what comes back', async () => {
    const { fixture, invoke } = render(part, vi.fn(async () => order));
    await fixture.whenStable();

    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();

    const quantity = fixture.nativeElement.querySelector('select[id^="return-qty-p1"]') as HTMLSelectElement;
    quantity.value = '1';
    quantity.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Refund of about ₹75');

    const upi = fixture.nativeElement.querySelector('input[placeholder="name@okicici"]') as HTMLInputElement;
    upi.value = 'asha@okicici';
    upi.dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1OrdersOrderIdPartsPartIdReturnPost, expect.objectContaining({
      body: expect.objectContaining({ items: [{ productId: 'p1', quantity: 1 }] }),
    }));
  });

  it('will not send nothing back', async () => {
    const { fixture } = render(part);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();

    const quantity = fixture.nativeElement.querySelector('select[id^="return-qty-p1"]') as HTMLSelectElement;
    quantity.value = '0';
    quantity.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect((fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('offers nothing once the window has closed', async () => {
    const { fixture } = render({ ...part, returnableUntilUtc: '2020-01-01T00:00:00Z' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('shows the seller\'s reason for refusing', async () => {
    const { fixture } = render({
      ...part,
      returnRequest: {
        status: 'Rejected',
        reason: 'NoLongerNeeded',
        comment: null,
        refundUpiId: 'asha@okicici',
        requestedAtUtc: '2026-09-23T10:00:00Z',
        decisionNote: 'Opened food cannot be taken back.',
        decidedAtUtc: '2026-09-23T12:00:00Z',
      },
    });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Opened food cannot be taken back.');
  });
});
