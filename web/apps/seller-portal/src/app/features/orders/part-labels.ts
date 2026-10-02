import { SellerOrderDto } from '@upbazaar/data-access';

/**
 * Headings and notes on the seller's order page that follow the part as it moves on, rather than
 * reading as if it were still waiting to be packed.
 */

/**
 * The heading over the part's items: "To pack" only while it is waiting to be packed; once
 * packed, what was packed; once handed to the courier, what was sent - even if some or all of it
 * later came back, which the notes beside each line and the status badge say.
 */
export function itemsTitleKey(status: string): string {
  switch (status) {
    case 'AwaitingPayment':
    case 'Confirmed':
      return 'sellerPortal.toPack';
    case 'Packed':
      return 'sellerPortal.packedItems';
    case 'Cancelled':
      return 'sellerPortal.cancelledItems';
    default:
      return 'sellerPortal.sentItems';
  }
}

/**
 * What a line's return units are doing: asked for while the request waits, coming back once it is
 * accepted, came back once the parcel is with the seller. Nothing when the request was refused or
 * the line has no units going back.
 */
export function lineReturnKey(
  order: Pick<SellerOrderDto, 'status' | 'returnRequest'>,
  returnQuantity: number | null | undefined
): string | null {
  if (!returnQuantity) {
    return null;
  }

  switch (order.returnRequest?.status) {
    case 'Requested':
      return 'returns.unitsAsked';
    case 'Approved':
      return order.status === 'Returned' ? 'returns.unitsCameBack' : 'returns.unitsBack';
    default:
      return null;
  }
}
