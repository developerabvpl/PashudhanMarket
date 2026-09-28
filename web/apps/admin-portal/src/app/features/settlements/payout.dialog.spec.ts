import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { CurrentUserStore } from '@upbazaar/auth';
import { Api, PayoutDto, apiV1AdminSettlementsPayoutsPayoutIdMarkPaidPost } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { PayoutDialog } from './payout.dialog';

const payout: PayoutDto = {
  id: 'p1',
  sellerId: 's1',
  shopName: 'UP Gaushala Collective',
  accountHolder: 'UP Gaushala Collective',
  accountNumber: '112233445566',
  ifsc: 'SBIN0001234',
  grossAmount: 150,
  commissionAmount: 15,
  tcsAmount: 0,
  tdsAmount: 0,
  courierCostAmount: 0,
  netAmount: 135,
  currency: 'INR',
  status: 'Pending',
  createdAtUtc: '2026-09-21T01:00:00Z',
  paidAtUtc: null,
  utr: null,
  paidBy: null,
  earnings: [],
};

describe('PayoutDialog', () => {
  it('shows the whole account to pay and records the UTR', async () => {
    const invoke = vi.fn(async () => ({ ...payout, status: 'Paid', utr: 'N123' }));
    const close = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n(translations),
        { provide: Api, useValue: { invoke } },
        { provide: MAT_DIALOG_DATA, useValue: payout },
        { provide: MatDialogRef, useValue: { close } },
        { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
      ],
    });

    const fixture = TestBed.createComponent(PayoutDialog);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('112233445566');
    expect(fixture.nativeElement.textContent).toContain('SBIN0001234');

    const utr = fixture.nativeElement.querySelector('input[name="utr"]') as HTMLInputElement;
    utr.value = ' N123 ';
    utr.dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminSettlementsPayoutsPayoutIdMarkPaidPost, { payoutId: 'p1', body: { utr: 'N123' } });
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ status: 'Paid' }));
  });
});
