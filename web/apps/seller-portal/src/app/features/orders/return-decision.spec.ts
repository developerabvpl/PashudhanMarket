import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, SellerOrderDto, apiV1SellerOrdersOrderIdPartsPartIdReturnDecisionPost } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { ReturnDecision } from './return-decision';

const order = {
  orderId: 'o1',
  orderNumber: 'UPB-260923-ABCDEF',
  partId: 'part1',
  status: 'Delivered',
  returnRequest: {
    status: 'Requested',
    reason: 'Damaged',
    comment: 'Two diyas arrived cracked.',
    refundUpiId: null,
    requestedAtUtc: '2026-09-23T10:00:00Z',
    decisionNote: null,
    decidedAtUtc: null,
  },
} as unknown as SellerOrderDto;

describe('ReturnDecision', () => {
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invoke = vi.fn(async () => ({ ...order, status: 'Returning' }));

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideI18n(translations), { provide: Api, useValue: { invoke } }],
    });
  });

  function button(fixture: { nativeElement: HTMLElement }, label: string): HTMLButtonElement {
    return [...fixture.nativeElement.querySelectorAll('button')].find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  }

  it('shows why the buyer wants to return it and accepts it', async () => {
    const fixture = TestBed.createComponent(ReturnDecision);
    fixture.componentRef.setInput('order', order);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('It arrived damaged');
    expect(fixture.nativeElement.textContent).toContain('Two diyas arrived cracked.');

    button(fixture, 'Accept return').click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerOrdersOrderIdPartsPartIdReturnDecisionPost, {
      orderId: 'o1',
      partId: 'part1',
      body: { approve: true, note: null },
    });
  });

  it('refuses only with a reason the buyer can read', async () => {
    const fixture = TestBed.createComponent(ReturnDecision);
    fixture.componentRef.setInput('order', order);
    await fixture.whenStable();

    expect(button(fixture, 'Refuse').disabled).toBe(true);

    const note = fixture.nativeElement.querySelector('input[name="note"]') as HTMLInputElement;
    note.value = 'Opened food cannot be taken back.';
    note.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    button(fixture, 'Refuse').click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerOrdersOrderIdPartsPartIdReturnDecisionPost, {
      orderId: 'o1',
      partId: 'part1',
      body: { approve: false, note: 'Opened food cannot be taken back.' },
    });
  });
});
