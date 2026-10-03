import { OrderPartDto } from '@upbazaar/data-access';
import { joinParts } from '@upbazaar/util';

/** A product that is coming, or came, back, and how many of it. */
export interface ReturnedLine {
  productId: string;
  name: string;
  quantity: number;
}

type ReturnablePart = Pick<OrderPartDto, 'lines' | 'returnRequest'>;

/**
 * What a parcel sends back to its seller.
 *
 * A buyer's return - asked for, or accepted - is the units the buyer named on each line: asking to
 * return one jar of three is one jar, whatever else the parcel held. It used to count as that only
 * once accepted, so a request still waiting for its decision listed the whole parcel - exactly when
 * staff most need to see what was actually asked for. A request made before partial returns named
 * no units and covers the whole parcel. A refused request sends nothing back.
 *
 * With no request it is a parcel the courier could not deliver, and all of it comes back.
 */
export function returnedLines(part: ReturnablePart): ReturnedLine[] {
  const request = part.returnRequest;

  if (request?.status === 'Rejected') {
    return [];
  }

  const named = part.lines.some((l) => (l.returnQuantity ?? 0) > 0);

  return part.lines
    .map((l) => ({ productId: l.productId, name: l.name, quantity: request && named ? (l.returnQuantity ?? 0) : l.quantity }))
    .filter((l) => l.quantity > 0);
}

/** Those lines as one phrase, "1 × Gobar Diya, 2 × Dhoop Batti": separated, where they used to run together. */
export function returnedLinesText(part: ReturnablePart): string {
  return joinParts(returnedLines(part).map((l) => `${l.quantity} × ${l.name}`));
}

/**
 * How to introduce them: asked for while the request waits, coming back once it is accepted (or the
 * courier turned round), came back once the parcel is with the seller.
 */
export function returnedLinesLabelKey(part: Pick<OrderPartDto, 'status' | 'returnRequest'>): string {
  if (part.returnRequest?.status === 'Requested') {
    return 'returns.itemsAsked';
  }

  return part.status === 'Returned' ? 'returns.itemsCameBack' : 'returns.itemsBack';
}
