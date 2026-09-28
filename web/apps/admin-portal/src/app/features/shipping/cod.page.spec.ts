import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  CodReceivableDto,
  CodRemittanceDto,
  apiV1AdminShippingCodReceivablesGet,
  apiV1AdminShippingCodReceivablesReceivableIdWriteOffPost,
  apiV1AdminShippingCodRemittancesGet,
  apiV1AdminShippingCodRemittancesPost,
  apiV1AdminShippingCodSummaryGet,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { CodPage } from './cod.page';

const owed: CodReceivableDto = {
  id: 'r1',
  orderId: 'o1',
  orderNumber: 'UPB-260925-ABCDEF',
  orderPartId: 'p1',
  sellerId: 's1',
  awb: 'AWB1',
  expected: 150,
  received: 140,
  status: 'ShortPaid',
  deliveredAtUtc: '2026-09-10T10:00:00Z',
  isOverdue: true,
  writeOffNote: null,
};

const remittance: CodRemittanceDto = {
  id: 'm1',
  reference: 'UTR123',
  remittedOn: '2026-09-25',
  total: 140,
  unmatchedCount: 1,
  fileName: 'report.csv',
  uploadedAtUtc: '2026-09-25T10:00:00Z',
  uploadedBy: 'finance',
  lines: [],
};

async function render(canWrite: boolean) {
  const invoke = vi.fn(async (fn: unknown) => {
    switch (fn) {
      case apiV1AdminShippingCodSummaryGet:
        return { outstandingAmount: 10, outstandingCount: 1, overdueCount: 1, shortCount: 1, overdueDays: 7 };
      case apiV1AdminShippingCodReceivablesGet:
        return { items: [owed], page: 1, pageSize: 100, totalCount: 1 };
      case apiV1AdminShippingCodRemittancesGet:
        return { items: [remittance], page: 1, pageSize: 25, totalCount: 1 };
      case apiV1AdminShippingCodRemittancesPost:
        return { ...remittance, lines: [{ awb: 'AWB1', amount: 140, matched: true, orderNumber: owed.orderNumber }] };
      default:
        return { ...owed, status: 'WrittenOff' };
    }
  });

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(translations),
      { provide: Api, useValue: { invoke } },
      { provide: CurrentUserStore, useValue: { has: () => canWrite, hasAll: () => canWrite } },
    ],
  });

  const fixture = TestBed.createComponent(CodPage);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

function fill(element: HTMLElement, name: string, value: string): void {
  const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function button(element: HTMLElement, text: string): HTMLButtonElement {
  return [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
}

describe('CodPage', () => {
  it('shows what the courier owes, and which parcels are overdue or short', async () => {
    const { element } = await render(true);

    expect(element.textContent).toContain('Overdue (over 7 days)');
    expect(element.textContent).toContain('UPB-260925-ABCDEF');
    expect(element.textContent).toContain('₹140.00 of ₹150.00 paid over');
    expect(element.textContent).toContain('1 not matched');
  });

  it('uploads a remittance report for a bank transfer', async () => {
    const { fixture, invoke, element } = await render(true);
    const file = new File(['AWB,Remitted Amount\nAWB1,140\n'], 'report.csv', { type: 'text/csv' });

    fill(element, 'reference', ' UTR123 ');
    fill(element, 'remittedOn', '2026-09-25');
    const input = element.querySelector('input[name="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminShippingCodRemittancesPost, {
      body: { reference: 'UTR123', remittedOn: '2026-09-25', file },
    });
  });

  it('writes off the rest of a short payment with a note', async () => {
    const { fixture, invoke, element } = await render(true);

    button(element, 'Write off the rest').click();
    await fixture.whenStable();
    fill(element, 'note', 'Courier confirmed the shortage.');
    await fixture.whenStable();
    button(element, 'Write off').click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminShippingCodReceivablesReceivableIdWriteOffPost, {
      receivableId: 'r1',
      body: { note: 'Courier confirmed the shortage.' },
    });
  });

  it('offers nothing to change to staff who may only look', async () => {
    const { element } = await render(false);

    expect(element.querySelector('input[name="reference"]')).toBeNull();
    expect(button(element, 'Write off the rest')).toBeUndefined();
  });
});
