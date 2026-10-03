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
 * How much of a parcel the buyer is sending, or sent, back: None, Partial (some units of an
 * accepted return) or Full. A parcel the courier brought back undelivered is None - that is not a
 * return. The API sends one per parcel with a list row (OrderSummaryDto.partReturns); for a whole
 * order {@link partReturn} works it out of the parcel's lines, by the same test.
 */
export type PartReturn = 'None' | 'Partial' | 'Full';

/**
 * How far an order has got, as one word for a list row or a page header.
 *
 * The order's own status stays Confirmed from payment until every parcel is delivered, so on its
 * own it calls an order already on a lorry "Confirmed". While Confirmed, the order reads as its
 * furthest parcel instead: Shipped once any parcel has left its seller (on the way, delivered,
 * or since sent back), else Packed once any is packed, else Confirmed. Cancelled parcels do not
 * count.
 *
 * A delivered order (Completed) stays Completed when the buyer sends some of it back, since the
 * goods did reach them - so on its own it calls an order whose parcel reads "Partly returned"
 * plain "Delivered". Given what is going back of each parcel, a Completed order with an accepted
 * return reads instead as:
 *  - Returned when everything that was delivered has come back to its seller, Returning while any
 *    of that is still on its way back;
 *  - PartlyReturned or PartlyReturning ("Delivered - partly returned") when the buyer keeps
 *    something: only some units of a parcel went back, or another parcel was delivered and kept.
 * Without `partReturns` - a caller that only knows statuses - a Completed order stays Completed.
 *
 * Any other order status - awaiting payment, cancelled - already says it all and is kept. Each
 * parcel's own status is shown beside it on the order's page.
 */
export function orderProgress(
  status: string,
  partStatuses: readonly string[] | null | undefined,
  partReturns?: readonly string[] | null
): string {
  if (!partStatuses) {
    return status;
  }

  if (status === 'Completed') {
    return returnProgress(partStatuses, partReturns ?? []) ?? status;
  }

  if (status !== 'Confirmed') {
    return status;
  }

  if (partStatuses.some((s) => LEFT_THE_SELLER.has(s))) {
    return 'Shipped';
  }

  return partStatuses.includes('Packed') ? 'Packed' : status;
}

/** A delivered order's accepted returns as one word, or null when the buyer sent nothing back. */
function returnProgress(partStatuses: readonly string[], partReturns: readonly string[]): string | null {
  const returned = partStatuses.filter((_, i) => (partReturns[i] ?? 'None') !== 'None');

  if (returned.length === 0) {
    return null;
  }

  // A delivered parcel that is not going back is kept whole; a partial return keeps the rest.
  const keepsSome = partReturns.includes('Partial') || partStatuses.includes('Delivered');
  const allBack = returned.every((s) => s === 'Returned');

  return (keepsSome ? 'Partly' : '') + (allBack ? 'Returned' : 'Returning');
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

/** What of the parcel the buyer is sending back, for {@link orderProgress}. */
export function partReturn(part: ReturnablePart): PartReturn {
  if (!isBuyerReturn(part)) {
    return 'None';
  }

  return isPartialReturn(part) ? 'Partial' : 'Full';
}

/**
 * The i18n key for a parcel's status: "Could not be delivered" for a courier return, "Being
 * returned" or "Returned" for a buyer's whole return, and "Partly being returned" or "Partly
 * returned" when the buyer kept some of it.
 */
export function partStatusKey(part: ReturnablePart): string {
  return partReturnStatusKey(part.status, partReturn(part));
}

/**
 * The same key for a caller that has no lines to look at - a row in a queue of return requests -
 * only the parcel's status and what the API says is going back of it (None, Partial or Full, as
 * {@link PartReturn}). Anything else, a response from before the API said, reads as None.
 */
export function partReturnStatusKey(status: string, returning: string | null | undefined): string {
  switch (returning) {
    case 'Partial':
      return `orders.returnStatus.Partly${status}`;
    case 'Full':
      return `orders.returnStatus.${status}`;
    default:
      return `orders.partStatus.${status}`;
  }
}
