import { OrderDto, OrderSummaryDto } from '@upbazaar/data-access';

/**
 * Why a cancelled order has no money to show: `notPaid` for an online order that ended without
 * being paid, `nothingToPay` for a cash-on-delivery order that never reached the door.
 */
export type UnpaidCancellation = 'notPaid' | 'nothingToPay';

/** The money a row in the buyer's order list shows. */
export interface OrderAmount {
  /** The headline figure; null for a cancelled order no money changed hands for, which shows {@link note} instead. */
  readonly amount: number | null;
  /** What is going back to the buyer, shown under it; null when nothing is. */
  readonly refund: number | null;
  /** What to say in place of an amount of ₹0, for an order cancelled before any money changed hands. */
  readonly note: UnpaidCancellation | null;
}

type AmountOrder = Pick<OrderSummaryDto, 'total' | 'amountPaid' | 'refundTotal'> &
  Partial<Pick<OrderSummaryDto, 'status' | 'paymentMethod' | 'cashCollected' | 'cashRefundTotal'>>;

type CancellableOrder = Pick<OrderDto, 'amountPaid'> & Partial<Pick<OrderDto, 'status' | 'paymentMethod' | 'cashCollected'>>;

/**
 * Whether the order was cancelled before any money changed hands, and which way it was to be paid.
 *
 * Such an order totals nothing, and "₹0" - in the list, or as "Subtotal ₹0, Total ₹0" on the
 * order's own page - reads as an order that cost nothing. The list and the Payment card both ask
 * here, so they tell the same story: an online order whose payment never came was "Not paid"; a
 * cash order cancelled, or brought back undelivered, before anything was paid at the door has
 * "Nothing to pay". Null for every other order, cancelled-after-paying included: that one has a
 * payment and a refund to show.
 */
export function unpaidCancellation(order: CancellableOrder): UnpaidCancellation | null {
  if (order.status !== 'Cancelled' || (order.amountPaid ?? null) !== null) {
    return null;
  }

  if (order.paymentMethod === 'Online') {
    return 'notPaid';
  }

  return order.paymentMethod === 'CashOnDelivery' && !((order.cashCollected ?? 0) > 0) ? 'nothingToPay' : null;
}

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
 * - A cash-on-delivery order cancelled before anything was paid at the door: "Nothing to pay",
 *   for the same reason.
 * - Anything else: its total.
 */
export function orderAmount(order: AmountOrder): OrderAmount {
  const paid = order.amountPaid ?? null;

  if (paid !== null) {
    return order.refundTotal > 0 ? { amount: paid, refund: order.refundTotal, note: null } : { amount: order.total, refund: null, note: null };
  }

  const cash = order.cashCollected ?? null;
  const cashRefund = order.cashRefundTotal ?? 0;

  if (cash !== null && cashRefund > 0) {
    return { amount: cash, refund: cashRefund, note: null };
  }

  const note = unpaidCancellation(order);

  return note ? { amount: null, refund: null, note } : { amount: order.total, refund: null, note: null };
}
