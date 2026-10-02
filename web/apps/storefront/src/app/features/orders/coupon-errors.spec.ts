import { ApiProblem } from '@upbazaar/data-access';
import { couponError, isCouponProblem } from './coupon-errors';

function problem(code: string, title: string): ApiProblem {
  return { status: 400, code, title, fieldErrors: {}, isValidation: false };
}

describe('couponError', () => {
  it('names a translated reason for each code the API gives', () => {
    expect(couponError(problem('promotions.coupon.already_used', 'You have already used this coupon'))).toEqual({
      key: 'checkout.coupon.errors.alreadyUsed',
      minimum: null,
    });
    expect(couponError(problem('promotions.coupon.unknown', 'x')).key).toBe('checkout.coupon.errors.unknown');
  });

  it('reads the minimum out of the below-minimum title', () => {
    const error = couponError(problem('promotions.coupon.below_minimum', 'That coupon needs at least Rs 499.5 of the goods it covers.'));

    expect(error).toEqual({ key: 'checkout.coupon.errors.belowMinimum', minimum: 499.5 });
  });

  it('still gives the reason when the minimum cannot be read', () => {
    expect(couponError(problem('promotions.coupon.below_minimum', 'Too small.')).key).toBe('checkout.coupon.errors.belowMinimumUnknown');
  });

  it('knows which failures are about the coupon, including Orders own free-delivery check', () => {
    expect(isCouponProblem(problem('promotions.coupon.below_minimum', 'x'))).toBe(true);
    expect(isCouponProblem(problem('orders.coupon.delivery_already_free', 'x'))).toBe(true);
    expect(couponError(problem('orders.coupon.delivery_already_free', 'x')).key).toBe('checkout.coupon.errors.deliveryAlreadyFree');
    expect(isCouponProblem(problem('orders.out_of_stock', 'x'))).toBe(false);
    expect(isCouponProblem(problem('toString', 'x'))).toBe(false);
  });

  it('falls back to the title for a code it does not know', () => {
    expect(couponError(problem('client.offline', 'errors.offline')).key).toBe('errors.offline');
  });
});
