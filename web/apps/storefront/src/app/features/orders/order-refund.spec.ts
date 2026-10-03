import { OrderDto } from '@upbazaar/data-access';
import { hasDelivery, orderRefund } from './order-refund';

type Parts = OrderDto['parts'];

const part = (status: string, returnRequest: unknown = null) =>
  ({ status, lines: [{ quantity: 2, returnQuantity: 1 }], returnRequest }) as unknown as Parts[number];

describe('orderRefund', () => {
  it('is what the API says is going back of an online payment', () => {
    expect(orderRefund({ paymentMethod: 'Online', amountPaid: 476, refundTotal: 79, parts: [] })).toEqual({
      amount: 79,
      to: 'payment',
      paid: 476,
    });
  });

  it('is nothing while an online order keeps everything it paid for', () => {
    expect(orderRefund({ paymentMethod: 'Online', amountPaid: 476, refundTotal: 0, parts: [] })).toBeNull();
    // Not paid yet: there is nothing to give back.
    expect(orderRefund({ paymentMethod: 'Online', amountPaid: null, refundTotal: 0, parts: [] })).toBeNull();
  });

  it('adds up the accepted returns of a cash on delivery order, which go to the buyer by UPI', () => {
    const parts = [
      part('Returned', { status: 'Approved', refundDue: 79 }),
      part('Returning', { status: 'Approved', refundDue: 120.5 }),
      part('Delivered', { status: 'Requested', refundDue: null }),
      part('Delivered', { status: 'Rejected', refundDue: null }),
    ];

    expect(orderRefund({ paymentMethod: 'CashOnDelivery', amountPaid: null, refundTotal: 0, parts })).toEqual({
      amount: 199.5,
      to: 'upi',
      paid: null,
    });
  });

  it('says what a cash buyer paid at the door beside what comes back, once the API tells it', () => {
    const parts = [part('Returned', { status: 'Approved', refundDue: 377 })];

    expect(orderRefund({ paymentMethod: 'CashOnDelivery', amountPaid: null, refundTotal: 0, cashCollected: 426, parts })).toEqual({
      amount: 377,
      to: 'upi',
      paid: 426,
    });
  });

  it('owes a cash buyer nothing for parcels that were cancelled or never delivered', () => {
    const parts = [part('Cancelled'), part('Returned')];

    expect(orderRefund({ paymentMethod: 'CashOnDelivery', amountPaid: null, refundTotal: 0, parts })).toBeNull();
  });
});

describe('hasDelivery', () => {
  it('is true while any parcel is the buyer’s or on its way to them', () => {
    expect(hasDelivery({ parts: [part('Cancelled'), part('Packed')] })).toBe(true);
    expect(hasDelivery({ parts: [part('Delivered')] })).toBe(true);
    // Delivered and then sent back by the buyer: it was delivered.
    expect(hasDelivery({ parts: [part('Returned', { status: 'Approved' })] })).toBe(true);
  });

  it('is false once every parcel was cancelled or came back undelivered', () => {
    expect(hasDelivery({ parts: [part('Cancelled')] })).toBe(false);
    expect(hasDelivery({ parts: [part('Cancelled'), part('Returned')] })).toBe(false);
    expect(hasDelivery({ parts: [] })).toBe(false);
  });
});
