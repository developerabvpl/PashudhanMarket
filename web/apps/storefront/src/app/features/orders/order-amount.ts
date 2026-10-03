import { OrderSummaryDto } from '@upbazaar/data-access';

/** The money a row in the buyer's order list shows. */
export interface OrderAmount {
  /** The headline figure; null for an order that was never paid for, which shows {@link unpaid} instead. */
  readonly amount: number | null;
  /** What is going back to the buyer, shown under it; null when nothing is. */
  readonly refund: number | null;
  /** True for an online order that ended without being paid: the row says "Not paid", not ₹0. */
  readonly unpaid: boolean;
}

type AmountOrder = Pick<OrderSummaryDto, 'total' | 'amountPaid' | 'refundTotal'> &
  Partial<Pick<OrderSummaryDto, 'status' | 'paymentMethod' | 'cashCollected' | 'cashRefundTotal'>>;

/**
 * The order's total counts only the goods the buyer keeps, so an order that was cancelled or sent
 * back totals little or nothing - true of the order, but alarming in a list to someone whose money
 * left their hands. So the row tells the story the order's own page tells:
 *
 * - Paid online, with something going back: what was paid, and the refund beneath.
 * - Cash on delivery, with a return accepted: what was paid at the door, and the refund to the
 *   buyer's UPI id beneath. It used to show the total - the delivery charge alone, ₹49, for an
 *   order of ₹426 returned in full.
 * - An online order that was cancelled without ever being paid (its payment window ran out):
 *   "Not paid" rather than a total of ₹0, which read as an order that cost nothing.
 * - Anything else: its total.
 */
export function orderAmount(order: AmountOrder): OrderAmount {
  const paid = order.amountPaid ?? null;

  if (paid !== null) {
    return order.refundTotal > 0 ? { amount: paid, refund: order.refundTotal, unpaid: false } : { amount: order.total, refund: null, unpaid: false };
  }

  const cash = order.cashCollected ?? null;
  const cashRefund = order.cashRefundTotal ?? 0;

  if (cash !== null && cashRefund > 0) {
    return { amount: cash, refund: cashRefund, unpaid: false };
  }

  if (order.status === 'Cancelled' && order.paymentMethod === 'Online') {
    return { amount: null, refund: null, unpaid: true };
  }

  return { amount: order.total, refund: null, unpaid: false };
}
