import { discountFundingKey, itemsTitleKey, lineReturnKey, partDiscount } from './part-labels';

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

    expect(lineReturnKey({ status: 'Returning', returnRequest: approved }, 2)).toBe('returns.unitsBack.other');
    expect(lineReturnKey({ status: 'Returned', returnRequest: approved }, 2)).toBe('returns.unitsCameBack.other');
    expect(lineReturnKey({ status: 'Delivered', returnRequest: { status: 'Requested' } as never }, 2)).toBe('returns.unitsAsked.other');
  });

  it('words one unit in the singular, which Hindi says differently from several', () => {
    const approved = { status: 'Approved' } as never;

    expect(lineReturnKey({ status: 'Returning', returnRequest: approved }, 1)).toBe('returns.unitsBack.one');
    expect(lineReturnKey({ status: 'Returned', returnRequest: approved }, 1)).toBe('returns.unitsCameBack.one');
    expect(lineReturnKey({ status: 'Delivered', returnRequest: { status: 'Requested' } as never }, 1)).toBe('returns.unitsAsked.one');
  });

  it('reads the coupon discount off the part, or adds up its lines when the part does not carry it', () => {
    const lines = [{ discount: 30 }, { discount: 20 }, {}] as never;

    expect(partDiscount({ discount: 50, lines })).toBe(50);
    expect(partDiscount({ lines })).toBe(50);
    expect(partDiscount({ discount: 0, lines: [] })).toBe(0);
  });

  it('says who funds a discount only when there is one and the API names them', () => {
    const lines = [] as never;

    expect(discountFundingKey({ discount: 50, lines, discountFundedBy: 'Seller' })).toBe('sellerPortal.discountFundedBy.Seller');
    expect(discountFundingKey({ discount: 50, lines, discountFundedBy: 'Platform' })).toBe('sellerPortal.discountFundedBy.Platform');
    expect(discountFundingKey({ discount: 50, lines, discountFundedBy: null })).toBeNull();
    expect(discountFundingKey({ discount: 0, lines, discountFundedBy: 'Seller' })).toBeNull();
  });

  it('notes nothing for a refused request or a line kept whole', () => {
    expect(lineReturnKey({ status: 'Delivered', returnRequest: { status: 'Rejected' } as never }, 1)).toBeNull();
    expect(lineReturnKey({ status: 'Returned', returnRequest: { status: 'Approved' } as never }, 0)).toBeNull();
  });
});
