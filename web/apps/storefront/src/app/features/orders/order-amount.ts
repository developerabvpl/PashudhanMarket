import { OrderSummaryDto } from '@upbazaar/data-access';

/** The money a row in the buyer's order list shows. */
export interface OrderAmount {
  /** The headline figure. */
  readonly amount: number;
  /** What is going back to the buyer, shown under it; null when nothing is. */
  readonly refund: number | null;
}

/**
 * The order's total counts only the goods the buyer keeps, so a paid order that was cancelled
 * totals ₹0 - true of the order, but alarming in a list to someone whose money left their account.
 * Once anything paid online is going back, the row shows what was paid with the refund beneath,
 * as the order's own page does; every other order shows its total.
 */
export function orderAmount(order: Pick<OrderSummaryDto, 'total' | 'amountPaid' | 'refundTotal'>): OrderAmount {
  const paid = order.amountPaid;

  return paid !== null && paid !== undefined && order.refundTotal > 0
    ? { amount: paid, refund: order.refundTotal }
    : { amount: order.total, refund: null };
}
