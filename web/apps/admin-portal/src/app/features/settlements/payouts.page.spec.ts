import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  PayoutSummaryDto,
  apiV1AdminSettlementsPayoutRunsPost,
  apiV1AdminSettlementsPayoutsGet,
} from '@upbazaar/data-access';
import { provideI18n, ToastService } from '@upbazaar/ui';
import { PayoutsPage } from './payouts.page';

const payout: PayoutSummaryDto = {
  id: 'p1',
  sellerId: 's1',
  shopName: 'UP Gaushala Collective',
  grossAmount: 1500,
  netAmount: 1350,
  earningCount: 4,
  currency: 'INR',
  status: 'Pending',
  createdAtUtc: '2026-09-21T01:00:00Z',
  paidAtUtc: null,
  utr: null,
};

describe('PayoutsPage', () => {
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invoke = vi.fn(async (fn: unknown) => {
      if (fn === apiV1AdminSettlementsPayoutsGet) {
        return { items: [payout], page: 1, pageSize: 25, totalCount: 1, hasNextPage: false, totalPages: 1 };
      }

      if (fn === apiV1AdminSettlementsPayoutRunsPost) {
        return { payoutsCreated: 2, earningsSettled: 5, sellersSkipped: 0 };
      }

      throw new Error('unexpected call');
    });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n(),
        { provide: Api, useValue: { invoke } },
        { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
      ],
    });
  });

  it('opens on the transfers still to make', async () => {
    const fixture = TestBed.createComponent(PayoutsPage);
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminSettlementsPayoutsGet, { Page: 1, PageSize: 25, Status: 'Pending' });
    expect(fixture.nativeElement.textContent).toContain('UP Gaushala Collective');
  });

  it('runs payouts on demand and says what it made', async () => {
    const fixture = TestBed.createComponent(PayoutsPage);
    await fixture.whenStable();

    const run = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: HTMLButtonElement) => b.textContent?.trim() === 'Run payouts now'
    ) as HTMLButtonElement;
    run.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminSettlementsPayoutRunsPost, {});
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('2 payouts made, covering 5 parcels.');
  });
});
