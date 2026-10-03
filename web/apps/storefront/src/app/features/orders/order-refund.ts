import { OrderDto, isBuyerReturn } from '@upbazaar/data-access';

/** Money going back to the buyer: how much, and by which road. */
export interface OrderRefund {
  readonly amount: number;
  /** `payment`: back through the online payment it came from. `upi`: sent to the buyer's UPI id. */
  readonly to: 'payment' | 'upi';
  /** What was paid online, shown above a refund of it; null for cash on delivery. */
  readonly paid: number | null;
}

type RefundableOrder = Pick<OrderDto, 'paymentMethod' | 'amountPaid' | 'refundTotal' | 'parts'>;

/**
 * What the Payment card says is going back, or null when nothing is.
 *
 * Paid online: the API's `refundTotal` - what of the amount paid the order no longer keeps
 * (cancelled parts, parcels that came back, accepted returns), owed from the moment the goods drop
 * out of the total. It goes back through the original payment.
 *
 * Cash on delivery: `refundTotal` is by definition a share of what was paid online, so it is zero -
 * yet a buyer who paid cash at the door and then returned goods is owed that cash, to the UPI id
 * they gave with the return. That is the sum of `refundDue` over the parcels' accepted return
 * requests: the same figure the parcel's own message quotes, and owed from acceptance just as an
 * online refund is. A cancelled or undelivered cash parcel collected nothing, so it adds nothing.
 * It is worked out here rather than folded into the API's `refundTotal`, which stays "of the amount
 * paid online" for the order list, the admin portal and Payments.
 */
export function orderRefund(order: RefundableOrder): OrderRefund | null {
  const paid = order.amountPaid ?? null;

  if (paid !== null) {
    return order.refundTotal > 0 ? { amount: order.refundTotal, to: 'payment', paid } : null;
  }

  if (order.paymentMethod !== 'CashOnDelivery') {
    return null;
  }

  const due = order.parts
    .filter((part) => part.returnRequest?.status === 'Approved')
    .reduce((sum, part) => sum + (part.returnRequest?.refundDue ?? 0), 0);

  return due > 0 ? { amount: due, to: 'upi', paid: null } : null;
}

/**
 * Whether anything in the order is the buyer's or still on its way to them: a parcel that is
 * neither cancelled nor brought back undelivered. The delivery charge of an order where nothing is
 * falls to zero because it was given back, not because delivery was free - so the card must not
 * call it "Free".
 */
export function hasDelivery(order: Pick<OrderDto, 'parts'>): boolean {
  return order.parts.some((part) => {
    const cameBackUndelivered = (part.status === 'Returning' || part.status === 'Returned') && !isBuyerReturn(part);

    return part.status !== 'Cancelled' && !cameBackUndelivered;
  });
}
