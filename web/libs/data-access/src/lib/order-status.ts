import type { OrderLineDto, ReturnRequestDto } from './api/models';

/**
 * Reading order and parcel statuses the same way in all three apps. The API keeps statuses
 * coarse on purpose - an order is Confirmed from payment to the last delivery, a parcel is
 * Returned whether one jar or all of it went back - and each screen needs a little more than that
 * to describe what the buyer, seller or support person actually sees.
 */

/** Statuses of a parcel that has left the seller: on its way, delivered, or coming back. */
const LEFT_THE_SELLER: ReadonlySet<string> = new Set(['Shipped', 'Delivered', 'Returning', 'Returned']);

/**
 * How far an order has got, as one word for a list row or a page header.
 *
 * The order's own status stays Confirmed from payment until every parcel is delivered, so on its
 * own it calls an order already on a lorry "Confirmed". While Confirmed, the order reads as its
 * furthest parcel instead: Shipped once any parcel has left its seller (on the way, delivered,
 * or since sent back), else Packed once any is packed, else Confirmed. Cancelled parcels do not
 * count. Any other order status - awaiting payment, delivered (Completed), cancelled - already
 * says it all and is kept. Each parcel's own status is shown beside it on the order's page.
 */
export function orderProgress(status: string, partStatuses: readonly string[] | null | undefined): string {
  if (status !== 'Confirmed' || !partStatuses) {
    return status;
  }

  if (partStatuses.some((s) => LEFT_THE_SELLER.has(s))) {
    return 'Shipped';
  }

  return partStatuses.includes('Packed') ? 'Packed' : status;
}

/** The parts of a parcel needed to tell what of it is going, or went, back. */
export interface ReturnablePart {
  readonly status: string;
  readonly lines: readonly Pick<OrderLineDto, 'quantity' | 'returnQuantity'>[];
  readonly returnRequest?: Pick<ReturnRequestDto, 'status'> | null;
}

/** Going or gone back because the buyer's return was accepted, rather than undelivered (RTO). */
export function isBuyerReturn(part: ReturnablePart): boolean {
  return part.returnRequest?.status === 'Approved' && (part.status === 'Returning' || part.status === 'Returned');
}

/**
 * A buyer's accepted return of only some of the parcel: fewer units of a line than were bought,
 * or not every line. The parcel's status is still Returning or Returned - it is the return
 * parcel that moves - so without this the whole parcel would read as returned. A request made
 * before partial returns named no units, and covers the whole parcel.
 */
export function isPartialReturn(part: ReturnablePart): boolean {
  const named = part.lines.some((l) => (l.returnQuantity ?? 0) > 0);

  return isBuyerReturn(part) && named && part.lines.some((l) => (l.returnQuantity ?? 0) < l.quantity);
}

/**
 * The i18n key for a parcel's status: "Could not be delivered" for a courier return, "Being
 * returned" or "Returned" for a buyer's whole return, and "Partly being returned" or "Partly
 * returned" when the buyer kept some of it.
 */
export function partStatusKey(part: ReturnablePart): string {
  if (!isBuyerReturn(part)) {
    return `orders.partStatus.${part.status}`;
  }

  return isPartialReturn(part) ? `orders.returnStatus.Partly${part.status}` : `orders.returnStatus.${part.status}`;
}
