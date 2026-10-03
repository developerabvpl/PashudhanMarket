import { orderAmount, unpaidCancellation } from './order-amount';

describe('orderAmount', () => {
  it('shows what was paid, and the refund, for a paid order that was cancelled', () => {
    expect(orderAmount({ total: 0, amountPaid: 476, refundTotal: 476 })).toEqual({ amount: 476, refund: 476, note: null });
  });

  it('shows what was paid, and the part coming back, when only part was cancelled', () => {
    expect(orderAmount({ total: 279, amountPaid: 359, refundTotal: 80 })).toEqual({ amount: 359, refund: 80, note: null });
  });

  it('shows the total when nothing is going back', () => {
    expect(orderAmount({ total: 359, amountPaid: 359, refundTotal: 0 })).toEqual({ amount: 359, refund: null, note: null });
    expect(orderAmount({ total: 120, amountPaid: null, refundTotal: 0 })).toEqual({ amount: 120, refund: null, note: null });
  });

  it('shows what was paid at the door, and the refund, for a cash order the buyer returned', () => {
    const returned = { total: 49, amountPaid: null, refundTotal: 0, status: 'Completed', paymentMethod: 'CashOnDelivery', cashCollected: 426, cashRefundTotal: 377 };

    expect(orderAmount(returned)).toEqual({ amount: 426, refund: 377, note: null });
  });

  it('shows the total of a cash order that keeps everything, delivered or not', () => {
    const cash = { total: 426, amountPaid: null, refundTotal: 0, status: 'Completed', paymentMethod: 'CashOnDelivery', cashRefundTotal: 0 };

    expect(orderAmount({ ...cash, cashCollected: 426 })).toEqual({ amount: 426, refund: null, note: null });
    expect(orderAmount({ ...cash, status: 'Confirmed', cashCollected: null })).toEqual({ amount: 426, refund: null, note: null });
  });

  it('says an online order that ran out of time was not paid, rather than that it cost nothing', () => {
    const expired = { total: 0, amountPaid: null, refundTotal: 0, status: 'Cancelled', paymentMethod: 'Online', cashCollected: null, cashRefundTotal: 0 };

    expect(orderAmount(expired)).toEqual({ amount: null, refund: null, note: 'notPaid' });
    // Still waiting for its payment: the total is what there is to pay.
    expect(orderAmount({ ...expired, status: 'PendingPayment', total: 426 })).toEqual({ amount: 426, refund: null, note: null });
  });

  it('says a cancelled cash order has nothing to pay, rather than that it cost nothing', () => {
    const cancelled = { total: 0, amountPaid: null, refundTotal: 0, status: 'Cancelled', paymentMethod: 'CashOnDelivery', cashCollected: null, cashRefundTotal: 0 };

    expect(orderAmount(cancelled)).toEqual({ amount: null, refund: null, note: 'nothingToPay' });
    expect(orderAmount({ ...cancelled, cashCollected: 0 })).toEqual({ amount: null, refund: null, note: 'nothingToPay' });
    // Still on its way: the total is what there is to pay at the door.
    expect(orderAmount({ ...cancelled, status: 'Confirmed', total: 257 })).toEqual({ amount: 257, refund: null, note: null });
  });
});

describe('unpaidCancellation', () => {
  it('is null for an order that was paid for, or is not cancelled', () => {
    expect(unpaidCancellation({ status: 'Cancelled', paymentMethod: 'Online', amountPaid: 476 })).toBeNull();
    expect(unpaidCancellation({ status: 'PendingPayment', paymentMethod: 'Online', amountPaid: null })).toBeNull();
    expect(unpaidCancellation({ status: 'Completed', paymentMethod: 'CashOnDelivery', amountPaid: null, cashCollected: 426 })).toBeNull();
  });

  it('tells an unpaid online order from a cash order that never reached the door', () => {
    expect(unpaidCancellation({ status: 'Cancelled', paymentMethod: 'Online', amountPaid: null })).toBe('notPaid');
    expect(unpaidCancellation({ status: 'Cancelled', paymentMethod: 'CashOnDelivery', amountPaid: null, cashCollected: null })).toBe('nothingToPay');
  });
});
