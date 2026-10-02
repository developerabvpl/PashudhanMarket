import { orderAmount } from './order-amount';

describe('orderAmount', () => {
  it('shows what was paid, and the refund, for a paid order that was cancelled', () => {
    expect(orderAmount({ total: 0, amountPaid: 476, refundTotal: 476 })).toEqual({ amount: 476, refund: 476 });
  });

  it('shows what was paid, and the part coming back, when only part was cancelled', () => {
    expect(orderAmount({ total: 279, amountPaid: 359, refundTotal: 80 })).toEqual({ amount: 359, refund: 80 });
  });

  it('shows the total when nothing is going back', () => {
    expect(orderAmount({ total: 359, amountPaid: 359, refundTotal: 0 })).toEqual({ amount: 359, refund: null });
    expect(orderAmount({ total: 120, amountPaid: null, refundTotal: 0 })).toEqual({ amount: 120, refund: null });
  });
});
