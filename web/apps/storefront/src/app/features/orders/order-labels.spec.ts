import { describe, expect, it } from 'vitest';
import en from '../../i18n/en.json';
import hi from '../../i18n/hi.json';
import { lineReturnKey, orderBadge, orderStatusBadge, partStatusBadge } from './order-labels';

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

  it('reads a delivered order the buyer sent some of back as partly returned, in the delivered colour', () => {
    expect(orderBadge('Completed', ['Returned'], ['Partial'])).toEqual({
      key: 'orders.status.PartlyReturned',
      tone: 'bg-success/15 text-ink',
    });
    expect(orderBadge('Completed', ['Returning'], ['Full']).key).toBe('orders.status.Returning');
    expect(orderBadge('Completed', ['Returned'], ['Full']).key).toBe('orders.status.Returned');
    expect(orderBadge('Completed', ['Delivered'], ['None']).key).toBe('orders.status.Completed');
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

describe('lineReturnKey', () => {
  const part = (status: string, request: string | null) => ({ status, lines: [], returnRequest: request ? { status: request } : null });

  it('says which units were asked back while the seller has not answered', () => {
    expect(lineReturnKey(part('Delivered', 'Requested'), 1)).toBe('orders.lineAsked.one');
    expect(lineReturnKey(part('Delivered', 'Requested'), 2)).toBe('orders.lineAsked.other');
  });

  it('follows an accepted return on its way back and once it is back, one unit or several', () => {
    expect(lineReturnKey(part('Returning', 'Approved'), 1)).toBe('orders.lineReturning.one');
    expect(lineReturnKey(part('Returning', 'Approved'), 3)).toBe('orders.lineReturning.other');
    expect(lineReturnKey(part('Returned', 'Approved'), 1)).toBe('orders.lineReturned.one');
    expect(lineReturnKey(part('Returned', 'Approved'), 3)).toBe('orders.lineReturned.other');
  });

  it('says nothing for a line that is not going back, a refused request, or an undelivered parcel', () => {
    expect(lineReturnKey(part('Delivered', 'Requested'), 0)).toBeNull();
    expect(lineReturnKey(part('Delivered', 'Rejected'), 1)).toBeNull();
    expect(lineReturnKey(part('Returned', null), 1)).toBeNull();
  });
});

describe('line return notes in Hindi', () => {
  const fill = (text: string, count: number, quantity: number) => text.replace('{{count}}', String(count)).replace('{{quantity}}', String(quantity));

  it('uses the singular for one unit and the plural for several', () => {
    expect(fill(hi.orders.lineReturned.one, 1, 3)).toBe('3 में से 1 वापस किया गया');
    expect(fill(hi.orders.lineReturned.other, 2, 3)).toBe('3 में से 2 वापस किए गए');
    expect(fill(hi.orders.lineReturning.one, 1, 3)).toBe('3 में से 1 वापस जा रहा है');
    expect(fill(hi.orders.lineReturning.other, 2, 3)).toBe('3 में से 2 वापस जा रहे हैं');
  });

  it('says how many were asked back, in both languages', () => {
    expect(fill(en.orders.lineAsked.one, 1, 3)).toBe('1 of 3 asked to return');
    expect(fill(hi.orders.lineAsked.one, 1, 3)).toContain('3 में से 1');
  });
});
