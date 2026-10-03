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
 *
 * Each note has a wording for one unit and for several (.one / .other), as cart.itemCount has:
 * Hindi says "1 वापस आ गया" but "2 वापस आ गए", and one key cannot be both.
 */
export function lineReturnKey(
  order: Pick<SellerOrderDto, 'status' | 'returnRequest'>,
  returnQuantity: number | null | undefined
): string | null {
  if (!returnQuantity) {
    return null;
  }

  const one = returnQuantity === 1;

  switch (order.returnRequest?.status) {
    case 'Requested':
      return one ? 'returns.unitsAsked.one' : 'returns.unitsAsked.other';
    case 'Approved':
      if (order.status === 'Returned') {
        return one ? 'returns.unitsCameBack.one' : 'returns.unitsCameBack.other';
      }

      return one ? 'returns.unitsBack.one' : 'returns.unitsBack.other';
    default:
      return null;
  }
}

/**
 * The coupon's discount on the seller's part. The API sends it with the part; a response from
 * before it did still carries each line's share, which adds up to the same figure.
 */
export function partDiscount(order: Pick<SellerOrderDto, 'discount' | 'lines'>): number {
  return order.discount ?? order.lines.reduce((sum, line) => sum + (line.discount ?? 0), 0);
}

/**
 * Who bears a discount on the part, as a sentence for the seller: it decides whether they are paid
 * on the discounted price (their own coupon, or a campaign they joined) or the full one (a coupon
 * the platform pays for). Null with no discount, or when the API does not say who funds it.
 */
export function discountFundingKey(order: Pick<SellerOrderDto, 'discount' | 'lines' | 'discountFundedBy'>): string | null {
  if (partDiscount(order) <= 0) {
    return null;
  }

  switch (order.discountFundedBy) {
    case 'Seller':
      return 'sellerPortal.discountFundedBy.Seller';
    case 'Platform':
      return 'sellerPortal.discountFundedBy.Platform';
    default:
      return null;
  }
}
