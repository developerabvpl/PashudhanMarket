import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  PaymentDto,
  RefundDto,
  apiV1AdminPaymentsGet,
  apiV1AdminPaymentsRefundsGet,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
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
  reasonCode: 'PaymentRefused',
  status: 'Due',
  method: 'Razorpay',
  upiId: null,
  gatewayPaymentId: 'pay_late',
  gatewayRefundId: null,
  createdAtUtc: '2026-09-22T10:00:00Z',
  refundedAtUtc: null,
  refundedBy: null,
};

const upiRefund: RefundDto = {
  ...refund,
  id: 'r2',
  paymentId: null,
  orderNumber: 'UPB-260923-PQRSTU',
  reason: 'The buyer returned the parcel and it is back with the seller.',
  reasonCode: 'BuyerReturn',
  method: 'Upi',
  upiId: 'asha@okicici',
  gatewayPaymentId: null,
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

/** Paid, confirmed its order, and the order was cancelled since: the money is on its way back. */
const cancelledPayment: PaymentDto = {
  ...payment,
  id: 'pmt3',
  orderNumber: 'UPB-260929-JPPXA9',
  amount: 476,
  orderOutcome: 'Cancelled',
  refundDue: 476,
};

function page<T>(items: T[]) {
  return { items, page: 1, pageSize: 25, totalCount: items.length, hasNextPage: false, totalPages: 1 };
}

describe('PaymentsPage', () => {
  let invoke: ReturnType<typeof vi.fn>;
  let refunds: RefundDto[];

  beforeEach(() => {
    refunds = [refund, upiRefund];
    invoke = vi.fn(async (fn: unknown) => {
      if (fn === apiV1AdminPaymentsRefundsGet) {
        return page(refunds);
      }

      if (fn === apiV1AdminPaymentsGet) {
        return page([payment, cancelledPayment]);
      }

      throw new Error('unexpected call');
    });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n(translations),
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

    // An online refund says where it goes in words, with the gateway's payment id beneath as the detail.
    const refundTo = [...fixture.nativeElement.querySelectorAll('td.mat-column-gatewayPaymentId')] as HTMLElement[];
    expect(refundTo[0].textContent?.replace(/\s+/g, ' ').trim()).toBe('Original payment (Razorpay) pay_late');
    expect(refundTo[0].querySelector('span.font-mono')?.textContent).toBe('pay_late');

    // Cash paid at the door goes back to the buyer's UPI id, not through Razorpay.
    expect(fixture.nativeElement.textContent).toContain('UPI to asha@okicici');
  });

  it('labels each refund from its reason code and keeps the stored sentence on hover', async () => {
    const fixture = TestBed.createComponent(PaymentsPage);
    await fixture.whenStable();

    const cells = [...fixture.nativeElement.querySelectorAll('td.mat-column-reason')] as HTMLElement[];
    expect(cells[0].textContent?.trim()).toBe('Paid, but the order could not take the payment');
    expect(cells[0].getAttribute('title')).toBe(refund.reason);
    expect(cells[1].textContent?.trim()).toBe('Returned by the buyer; back with the seller');
  });

  it('shows the reason in Hindi when the portal is in Hindi', async () => {
    const fixture = TestBed.createComponent(PaymentsPage);
    await fixture.whenStable();

    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));
    await fixture.whenStable();

    const cells = [...fixture.nativeElement.querySelectorAll('td.mat-column-reason')] as HTMLElement[];
    expect(cells[0].textContent?.trim()).toBe('भुगतान हुआ, पर ऑर्डर उसे नहीं ले सका');

    const refundTo = [...fixture.nativeElement.querySelectorAll('td.mat-column-gatewayPaymentId')] as HTMLElement[];
    expect(refundTo[0].textContent).toContain('मूल भुगतान (Razorpay)');
    expect(refundTo[1].textContent).toContain('asha@okicici');
    expect(cells[1].textContent?.trim()).toBe('खरीदार ने लौटाया; विक्रेता के पास वापस');
  });

  it('falls back to the stored sentence when the code is missing or not one it knows', async () => {
    refunds = [
      { ...refund, id: 'r3', reasonCode: null, reason: 'Refunded by hand before codes existed.' },
      { ...refund, id: 'r4', reasonCode: 'SomethingNew', reason: 'A reason added on the server later.' },
    ];

    const fixture = TestBed.createComponent(PaymentsPage);
    await fixture.whenStable();

    const cells = [...fixture.nativeElement.querySelectorAll('td.mat-column-reason')] as HTMLElement[];
    expect(cells[0].textContent?.trim()).toBe('Refunded by hand before codes existed.');
    expect(cells[1].textContent?.trim()).toBe('A reason added on the server later.');
    expect(fixture.nativeElement.textContent).not.toContain('payments.reasons');
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

  it('says when the order a payment confirmed has been cancelled since', async () => {
    const fixture = TestBed.createComponent(PaymentsPage);
    await fixture.whenStable();

    const toggle = [...fixture.nativeElement.querySelectorAll('button')].find(
      (button: HTMLButtonElement) => button.closest('mat-button-toggle')?.getAttribute('value') === 'payments'
    ) as HTMLButtonElement;

    toggle.click();
    await fixture.whenStable();

    const rows = [...fixture.nativeElement.querySelectorAll('tr[mat-row]')] as HTMLElement[];
    expect(rows[0].textContent).toContain('Order confirmed');
    expect(rows[1].textContent).toContain('Order cancelled');
    expect(rows[1].textContent).not.toContain('Order confirmed');
  });
});
