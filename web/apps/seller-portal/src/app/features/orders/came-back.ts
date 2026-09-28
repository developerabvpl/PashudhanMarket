import { OrderLineDto, SellerOrderDto } from '@upbazaar/data-access';

/** A product that is coming, or came, back, and how many of it. */
export interface ReturnedLine {
  productId: string;
  name: string;
  quantity: number;
}

/**
 * What goes back to the seller: the units of a buyer's approved return, or all of a parcel the
 * courier could not deliver. Requests made before partial returns named no units, and cover the
 * whole parcel.
 */
export function cameBack(order: Pick<SellerOrderDto, 'lines' | 'returnRequest'>): ReturnedLine[] {
  const buyerReturn = order.returnRequest?.status === 'Approved';
  const named = order.lines.some((l) => (l.returnQuantity ?? 0) > 0);

  return order.lines
    .map((l: OrderLineDto) => ({
      productId: l.productId,
      name: l.name,
      quantity: buyerReturn && named ? (l.returnQuantity ?? 0) : l.quantity,
    }))
    .filter((l) => l.quantity > 0);
}
