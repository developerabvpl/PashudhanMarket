/**
 * How an order or part status reads on screen: an i18n key and a badge colour.
 *
 * The API sends statuses as plain strings. Keeping the mapping in one place means the list, the
 * detail page and the confirmation all colour a Cancelled order the same way, and a status the
 * storefront does not know yet still renders as a neutral badge rather than breaking the page.
 */
export interface StatusBadge {
  readonly key: string;
  readonly tone: string;
}

const TONES: Readonly<Record<string, string>> = {
  PendingPayment: 'bg-warning/15 text-ink',
  AwaitingPayment: 'bg-warning/15 text-ink',
  Confirmed: 'bg-brand-50 text-brand-800',
  Packed: 'bg-brand-50 text-brand-800',
  Shipped: 'bg-info/15 text-ink',
  Delivered: 'bg-success/15 text-ink',
  Completed: 'bg-success/15 text-ink',
  Cancelled: 'bg-surface-sunken text-ink-muted',
};

const NEUTRAL = 'bg-surface-sunken text-ink';

export function orderStatusBadge(status: string): StatusBadge {
  return { key: `orders.status.${status}`, tone: TONES[status] ?? NEUTRAL };
}

export function partStatusBadge(status: string): StatusBadge {
  return { key: `orders.partStatus.${status}`, tone: TONES[status] ?? NEUTRAL };
}
