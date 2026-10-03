import { orderAmount } from './order-amount';

describe('orderAmount', () => {
  it('shows what was paid, and the refund, for a paid order that was cancelled', () => {
    expect(orderAmount({ total: 0, amountPaid: 476, refundTotal: 476 })).toEqual({ amount: 476, refund: 476, unpaid: false });
  });

  it('shows what was paid, and the part coming back, when only part was cancelled', () => {
    expect(orderAmount({ total: 279, amountPaid: 359, refundTotal: 80 })).toEqual({ amount: 359, refund: 80, unpaid: false });
  });

  it('shows the total when nothing is going back', () => {
    expect(orderAmount({ total: 359, amountPaid: 359, refundTotal: 0 })).toEqual({ amount: 359, refund: null, unpaid: false });
    expect(orderAmount({ total: 120, amountPaid: null, refundTotal: 0 })).toEqual({ amount: 120, refund: null, unpaid: false });
  });

  it('shows what was paid at the door, and the refund, for a cash order the buyer returned', () => {
    const returned = { total: 49, amountPaid: null, refundTotal: 0, status: 'Completed', paymentMethod: 'CashOnDelivery', cashCollected: 426, cashRefundTotal: 377 };

    expect(orderAmount(returned)).toEqual({ amount: 426, refund: 377, unpaid: false });
  });

  it('shows the total of a cash order that keeps everything, delivered or not', () => {
    const cash = { total: 426, amountPaid: null, refundTotal: 0, status: 'Completed', paymentMethod: 'CashOnDelivery', cashRefundTotal: 0 };

    expect(orderAmount({ ...cash, cashCollected: 426 })).toEqual({ amount: 426, refund: null, unpaid: false });
    expect(orderAmount({ ...cash, status: 'Confirmed', cashCollected: null })).toEqual({ amount: 426, refund: null, unpaid: false });
  });

  it('says an online order that ran out of time was not paid, rather than that it cost nothing', () => {
    const expired = { total: 0, amountPaid: null, refundTotal: 0, status: 'Cancelled', paymentMethod: 'Online', cashCollected: null, cashRefundTotal: 0 };

    expect(orderAmount(expired)).toEqual({ amount: null, refund: null, unpaid: true });
    // Still waiting for its payment: the total is what there is to pay.
    expect(orderAmount({ ...expired, status: 'PendingPayment', total: 426 })).toEqual({ amount: 426, refund: null, unpaid: false });
    // A cancelled cash order owed nothing and collected nothing; its total says so.
    expect(orderAmount({ ...expired, paymentMethod: 'CashOnDelivery' })).toEqual({ amount: 0, refund: null, unpaid: false });
  });
});
