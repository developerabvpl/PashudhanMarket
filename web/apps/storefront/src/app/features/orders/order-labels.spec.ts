import { describe, expect, it } from 'vitest';
import { orderStatusBadge, partStatusBadge } from './order-labels';

describe('order status badges', () => {
  it('maps a known status to its i18n key and colour', () => {
    expect(orderStatusBadge('Cancelled')).toEqual({
      key: 'orders.status.Cancelled',
      tone: 'bg-surface-sunken text-ink-muted',
    });
    expect(partStatusBadge('Shipped').key).toBe('orders.partStatus.Shipped');
  });

  it('still renders a status the storefront does not know yet, in a neutral colour', () => {
    expect(orderStatusBadge('OnHold').tone).toBe('bg-surface-sunken text-ink');
  });
});
