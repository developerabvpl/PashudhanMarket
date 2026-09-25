import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import {
  Api,
  apiV1SellerSettlementsBalanceGet,
  apiV1SellerSettlementsEarningsGet,
  apiV1SellerSettlementsPayoutsGet,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { EarningsPage } from './earnings.page';

describe('EarningsPage', () => {
  it('splits the balance by where the money is and lists each parcel earned from', async () => {
    const invoke = vi.fn(async (fn: unknown) => {
      switch (fn) {
        case apiV1SellerSettlementsBalanceGet:
          return { accruingAmount: 135, onHoldAmount: 0, payableAmount: 270, pendingPayoutAmount: 0, paidAmount: 1000, currency: 'INR' };
        case apiV1SellerSettlementsPayoutsGet:
          return { items: [], page: 1, pageSize: 10, totalCount: 0, hasNextPage: false, totalPages: 0 };
        case apiV1SellerSettlementsEarningsGet:
          return {
            items: [
              {
                id: 'e1',
                sellerId: 's1',
                orderId: 'o1',
                orderNumber: 'UPB-260923-ABCDEF',
                orderPartId: 'part1',
                kind: 'Sale',
                grossAmount: 150,
                commissionPercent: 10,
                commissionAmount: 15,
                tcsAmount: 0,
                tdsAmount: 0,
                netAmount: 135,
                currency: 'INR',
                status: 'Accruing',
                deliveredAtUtc: '2026-09-23T10:00:00Z',
                payableFromUtc: '2026-09-30T10:00:00Z',
                payoutId: null,
              },
            ],
            page: 1,
            pageSize: 25,
            totalCount: 1,
            hasNextPage: false,
            totalPages: 1,
          };
        default:
          throw new Error('unexpected call');
      }
    });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideI18n(), { provide: Api, useValue: { invoke } }],
    });

    const fixture = TestBed.createComponent(EarningsPage);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain("In next Monday's payout");
    expect(text).toContain('UPB-260923-ABCDEF');
    expect(text).toContain('In return window');
    expect(text).toContain('(10%)');
  });
});
