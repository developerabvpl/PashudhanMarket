import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  PaymentDto,
  RefundDto,
  apiV1AdminPaymentsGet,
  apiV1AdminPaymentsRefundsGet,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { PaymentsPage } from './payments.page';

const refund: RefundDto = {
  id: 'r1',
  paymentId: 'pmt1',
  orderId: 'o1',
  orderNumber: 'UPB-260922-ABCDEF',
  orderPartId: null,
  amount: 160,
  currency: 'INR',
  reason: 'Payment could not be applied to the order: This order is not waiting for payment.',
  status: 'Due',
  gatewayPaymentId: 'pay_late',
  gatewayRefundId: null,
  createdAtUtc: '2026-09-22T10:00:00Z',
  refundedAtUtc: null,
  refundedBy: null,
};

const payment: PaymentDto = {
  id: 'pmt2',
  orderId: 'o2',
  orderNumber: 'UPB-260922-GHJKMN',
  buyerId: 'b1',
  amount: 139,
  currency: 'INR',
  status: 'Paid',
  orderOutcome: 'Confirmed',
  gateway: 'Razorpay',
  gatewayOrderId: 'order_abc',
  gatewayPaymentId: 'pay_abc',
  lastFailure: null,
  createdAtUtc: '2026-09-22T09:00:00Z',
  paidAtUtc: '2026-09-22T09:01:00Z',
  refundDue: 0,
};

function page<T>(items: T[]) {
  return { items, page: 1, pageSize: 25, totalCount: items.length, hasNextPage: false, totalPages: 1 };
}

describe('PaymentsPage', () => {
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invoke = vi.fn(async (fn: unknown) => {
      if (fn === apiV1AdminPaymentsRefundsGet) {
        return page([refund]);
      }

      if (fn === apiV1AdminPaymentsGet) {
        return page([payment]);
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

  it('opens on the refunds still owed, asking only for Due ones', async () => {
    const fixture = TestBed.createComponent(PaymentsPage);
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminPaymentsRefundsGet, { Page: 1, PageSize: 25, Status: 'Due' });
    expect(fixture.nativeElement.textContent).toContain('UPB-260922-ABCDEF');
    expect(fixture.nativeElement.textContent).toContain('pay_late');
  });

  it('shows every payment with its Razorpay ids on the other view', async () => {
    const fixture = TestBed.createComponent(PaymentsPage);
    await fixture.whenStable();

    const toggle = [...fixture.nativeElement.querySelectorAll('button')].find(
      (button: HTMLButtonElement) => button.closest('mat-button-toggle')?.getAttribute('value') === 'payments'
    ) as HTMLButtonElement;

    toggle.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminPaymentsGet, { Page: 1, PageSize: 25, Search: undefined });
    expect(fixture.nativeElement.textContent).toContain('order_abc');
    expect(fixture.nativeElement.textContent).toContain('pay_abc');
  });
});
