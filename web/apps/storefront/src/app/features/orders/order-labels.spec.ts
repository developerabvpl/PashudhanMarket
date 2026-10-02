import { describe, expect, it } from 'vitest';
import { orderBadge, orderStatusBadge, partStatusBadge } from './order-labels';

describe('order status badges', () => {
  it('maps a known status to its i18n key and colour', () => {
    expect(orderStatusBadge('Cancelled')).toEqual({
      key: 'orders.status.Cancelled',
      tone: 'bg-surface-sunken text-ink-muted',
    });
    expect(partStatusBadge({ status: 'Shipped', lines: [] }).key).toBe('orders.partStatus.Shipped');
  });

  it('reads a confirmed order as shipped once a parcel is on its way', () => {
    expect(orderBadge('Confirmed', ['Shipped', 'Confirmed'])).toEqual({ key: 'orders.status.Shipped', tone: 'bg-info/15 text-ink' });
    expect(orderBadge('Confirmed', ['Confirmed']).key).toBe('orders.status.Confirmed');
  });

  it('words a return the buyer asked for apart from one the courier brought back', () => {
    const lines = [{ quantity: 1, returnQuantity: 1 }];

    expect(partStatusBadge({ status: 'Returning', lines }).key).toBe('orders.partStatus.Returning');
    expect(partStatusBadge({ status: 'Returning', lines, returnRequest: { status: 'Approved' } }).key).toBe('orders.returnStatus.Returning');
    expect(partStatusBadge({ status: 'Delivered', lines, returnRequest: { status: 'Approved' } }).key).toBe('orders.partStatus.Delivered');
  });

  it('says partly returned when the buyer kept some of the parcel', () => {
    const part = { status: 'Returned', lines: [{ quantity: 3, returnQuantity: 1 }], returnRequest: { status: 'Approved' } };

    expect(partStatusBadge(part)).toEqual({ key: 'orders.returnStatus.PartlyReturned', tone: 'bg-surface-sunken text-ink-muted' });
  });

  it('still renders a status the storefront does not know yet, in a neutral colour', () => {
    expect(orderStatusBadge('OnHold').tone).toBe('bg-surface-sunken text-ink');
  });
});
