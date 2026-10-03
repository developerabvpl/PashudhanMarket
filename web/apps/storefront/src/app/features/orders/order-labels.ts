import { ReturnablePart, orderProgress, partStatusKey } from '@upbazaar/data-access';

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
  Returning: 'bg-warning/15 text-ink',
  Returned: 'bg-surface-sunken text-ink-muted',
  // A delivered order the buyer sent some of back is still theirs in part: it keeps the delivered green.
  PartlyReturning: 'bg-success/15 text-ink',
  PartlyReturned: 'bg-success/15 text-ink',
};

const NEUTRAL = 'bg-surface-sunken text-ink';

export function orderStatusBadge(status: string): StatusBadge {
  return { key: `orders.status.${status}`, tone: TONES[status] ?? NEUTRAL };
}

/**
 * An order's badge in the list and on its page: how far its parcels have got, by the rule in
 * {@link orderProgress} - a confirmed order reads Shipped once any parcel has left its seller,
 * Packed once any is packed, and a delivered one the buyer sent back reads Returned or "Delivered ·
 * partly returned" - so the two never disagree. The list passes the summary's partReturns; the
 * order's page works them out of its parcels with partReturn.
 */
export function orderBadge(
  status: string,
  partStatuses: readonly string[] | null | undefined,
  partReturns?: readonly string[] | null
): StatusBadge {
  return orderStatusBadge(orderProgress(status, partStatuses, partReturns));
}

/**
 * A part going or gone back reads differently depending on why: "could not be delivered" for a
 * courier return, "being returned" for one the buyer asked for and the seller accepted, and
 * "partly returned" when the buyer sent back only some of it.
 */
export function partStatusBadge(part: ReturnablePart): StatusBadge {
  return { key: partStatusKey(part), tone: TONES[part.status] ?? NEUTRAL };
}

/**
 * What a line's return units are doing, as the note under it: asked for while the seller has not
 * answered, going back once the return is accepted, returned once the parcel is with the seller.
 * Nothing for a line with no units going back, or when the request was refused.
 *
 * It starts at the request, not the acceptance: a buyer who asked to return one jar of three sees
 * which one they asked about while they wait. Each note has a wording for one unit and for several
 * (.one / .other), as cart.itemCount has - Hindi says "1 वापस किया गया" but "2 वापस किए गए".
 */
export function lineReturnKey(part: ReturnablePart, returnQuantity: number | null | undefined): string | null {
  if (!returnQuantity) {
    return null;
  }

  const one = returnQuantity === 1;

  switch (part.returnRequest?.status) {
    case 'Requested':
      return one ? 'orders.lineAsked.one' : 'orders.lineAsked.other';
    case 'Approved':
      if (part.status === 'Returned') {
        return one ? 'orders.lineReturned.one' : 'orders.lineReturned.other';
      }

      return part.status === 'Returning' ? (one ? 'orders.lineReturning.one' : 'orders.lineReturning.other') : null;
    default:
      return null;
  }
}
