import { itemsTitleKey, lineReturnKey } from './part-labels';

describe('seller part labels', () => {
  it('heads the items "To pack" only until the part is packed', () => {
    expect(itemsTitleKey('Confirmed')).toBe('sellerPortal.toPack');
    expect(itemsTitleKey('Packed')).toBe('sellerPortal.packedItems');
    expect(itemsTitleKey('Shipped')).toBe('sellerPortal.sentItems');
    expect(itemsTitleKey('Returned')).toBe('sellerPortal.sentItems');
    expect(itemsTitleKey('Cancelled')).toBe('sellerPortal.cancelledItems');
  });

  it('says the units came back once the return has arrived', () => {
    const approved = { status: 'Approved' } as never;

    expect(lineReturnKey({ status: 'Returning', returnRequest: approved }, 1)).toBe('returns.unitsBack');
    expect(lineReturnKey({ status: 'Returned', returnRequest: approved }, 1)).toBe('returns.unitsCameBack');
    expect(lineReturnKey({ status: 'Delivered', returnRequest: { status: 'Requested' } as never }, 1)).toBe('returns.unitsAsked');
  });

  it('notes nothing for a refused request or a line kept whole', () => {
    expect(lineReturnKey({ status: 'Delivered', returnRequest: { status: 'Rejected' } as never }, 1)).toBeNull();
    expect(lineReturnKey({ status: 'Returned', returnRequest: { status: 'Approved' } as never }, 0)).toBeNull();
  });
});
