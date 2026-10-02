import { isPartialReturn, orderProgress, partStatusKey } from './order-status';

const line = (quantity: number, returnQuantity = 0) => ({ quantity, returnQuantity });

describe('orderProgress', () => {
  it('reads a confirmed order as its furthest parcel', () => {
    expect(orderProgress('Confirmed', ['Confirmed'])).toBe('Confirmed');
    expect(orderProgress('Confirmed', ['Confirmed', 'Packed'])).toBe('Packed');
    expect(orderProgress('Confirmed', ['Packed', 'Shipped'])).toBe('Shipped');
    expect(orderProgress('Confirmed', ['Confirmed', 'Delivered'])).toBe('Shipped');
    expect(orderProgress('Confirmed', ['Returning'])).toBe('Shipped');
  });

  it('ignores cancelled parcels', () => {
    expect(orderProgress('Confirmed', ['Cancelled', 'Confirmed'])).toBe('Confirmed');
  });

  it('keeps an order status that already says it all', () => {
    expect(orderProgress('Completed', ['Delivered'])).toBe('Completed');
    expect(orderProgress('Cancelled', ['Cancelled'])).toBe('Cancelled');
    expect(orderProgress('PendingPayment', ['AwaitingPayment'])).toBe('PendingPayment');
    expect(orderProgress('Confirmed', undefined)).toBe('Confirmed');
  });
});

describe('parcel status keys', () => {
  const approved = { status: 'Approved' };

  it('words a courier return apart from a buyer return', () => {
    expect(partStatusKey({ status: 'Returned', lines: [line(3)], returnRequest: null })).toBe('orders.partStatus.Returned');
    expect(partStatusKey({ status: 'Returned', lines: [line(3, 3)], returnRequest: approved })).toBe('orders.returnStatus.Returned');
    expect(partStatusKey({ status: 'Shipped', lines: [line(3)] })).toBe('orders.partStatus.Shipped');
  });

  it('says partly returned when the buyer sent back only some units', () => {
    const part = { status: 'Returned', lines: [line(3, 1)], returnRequest: approved };

    expect(isPartialReturn(part)).toBe(true);
    expect(partStatusKey(part)).toBe('orders.returnStatus.PartlyReturned');
    expect(partStatusKey({ ...part, status: 'Returning' })).toBe('orders.returnStatus.PartlyReturning');
  });

  it('says partly returned when one line of several went back whole', () => {
    expect(isPartialReturn({ status: 'Returned', lines: [line(2, 2), line(1)], returnRequest: approved })).toBe(true);
  });

  it('treats a request that named no units as the whole parcel', () => {
    expect(isPartialReturn({ status: 'Returned', lines: [line(3)], returnRequest: approved })).toBe(false);
  });

  it('is not a return at all until the request is accepted', () => {
    expect(partStatusKey({ status: 'Delivered', lines: [line(3, 1)], returnRequest: { status: 'Requested' } })).toBe(
      'orders.partStatus.Delivered'
    );
  });
});
