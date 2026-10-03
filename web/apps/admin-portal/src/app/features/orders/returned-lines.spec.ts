import { OrderLineDto, OrderPartDto, ReturnRequestDto } from '@upbazaar/data-access';
import { returnedLines, returnedLinesLabelKey, returnedLinesText } from './returned-lines';

function line(name: string, quantity: number, returnQuantity = 0): OrderLineDto {
  return { productId: name, sku: name, name, unitPrice: 100, quantity, lineTotal: 100 * quantity, discount: 0, returnQuantity };
}

function request(status: string): ReturnRequestDto {
  return { status, reason: 'Damaged', comment: null, refundUpiId: null, requestedAtUtc: '2026-10-01T10:00:00Z', decisionNote: null, decidedAtUtc: null };
}

function part(lines: OrderLineDto[], returnRequest: ReturnRequestDto | null, status = 'Delivered'): Pick<OrderPartDto, 'lines' | 'returnRequest' | 'status'> {
  return { lines, returnRequest, status };
}

describe('returnedLines', () => {
  const lines = [line('Gaunyl', 2, 1), line('Kande', 1, 0)];

  it('lists only the units the buyer asked to return while the request waits for a decision', () => {
    const waiting = part(lines, request('Requested'));

    expect(returnedLines(waiting)).toEqual([{ productId: 'Gaunyl', name: 'Gaunyl', quantity: 1 }]);
    expect(returnedLinesText(waiting)).toBe('1 × Gaunyl');
    expect(returnedLinesLabelKey(waiting)).toBe('returns.itemsAsked');
  });

  it('lists the same units once the return is accepted and once it is back', () => {
    expect(returnedLinesText(part(lines, request('Approved'), 'Returning'))).toBe('1 × Gaunyl');
    expect(returnedLinesLabelKey(part(lines, request('Approved'), 'Returning'))).toBe('returns.itemsBack');
    expect(returnedLinesLabelKey(part(lines, request('Approved'), 'Returned'))).toBe('returns.itemsCameBack');
  });

  it('separates several products', () => {
    expect(returnedLinesText(part([line('Gaunyl', 2, 2), line('Kande', 1, 1)], request('Requested')))).toBe('2 × Gaunyl, 1 × Kande');
  });

  it('falls back to the whole parcel for an old request that named no units', () => {
    expect(returnedLinesText(part([line('Gaunyl', 2), line('Kande', 1)], request('Requested')))).toBe('2 × Gaunyl, 1 × Kande');
  });

  it('lists all of a parcel the courier could not deliver', () => {
    expect(returnedLinesText(part([line('Gaunyl', 2), line('Kande', 1)], null, 'Returned'))).toBe('2 × Gaunyl, 1 × Kande');
  });

  it('lists nothing for a refused request', () => {
    expect(returnedLines(part(lines, request('Rejected')))).toEqual([]);
  });
});
