import { ApiProblem } from '@upbazaar/data-access';
import { placementFailure } from './placement-errors';

const SHOWN = ['fullName', 'mobile', 'line1', 'city', 'state', 'pincode'];

function problem(code: string, title: string, overrides: Partial<ApiProblem> = {}): ApiProblem {
  return { status: 409, code, title, fieldErrors: {}, isValidation: false, ...overrides };
}

describe('placementFailure', () => {
  it('sends every coupon refusal to the coupon box, translated', () => {
    expect(placementFailure(problem('promotions.coupon.used_up', 'Used up.', { status: 400 }), SHOWN)).toEqual({
      kind: 'coupon',
      error: { key: 'checkout.coupon.errors.usedUp', minimum: null },
    });
    expect(placementFailure(problem('orders.coupon.delivery_already_free', 'Free already.', { status: 400 }), SHOWN)).toEqual({
      kind: 'coupon',
      error: { key: 'checkout.coupon.errors.deliveryAlreadyFree', minimum: null },
    });
  });

  it('toasts the basket and stock failures in the buyer language', () => {
    expect(placementFailure(problem('orders.out_of_stock', 'Sold out.'), SHOWN)).toEqual({
      kind: 'toast',
      message: 'checkout.errors.outOfStock',
      code: 'orders.out_of_stock',
    });
    expect(placementFailure(problem('cart.not_ready_for_checkout', 'Changed.'), SHOWN)).toMatchObject({ message: 'checkout.errors.cartChanged' });
    expect(placementFailure(problem('cart.empty', 'Empty.'), SHOWN)).toMatchObject({ message: 'checkout.errors.cartEmpty' });
  });

  it('falls back to the API title, never hiding a failure it does not know', () => {
    expect(placementFailure(problem('inventory.something_new', 'Something new went wrong.'), SHOWN)).toEqual({
      kind: 'toast',
      message: 'Something new went wrong.',
      code: 'inventory.something_new',
    });
    expect(placementFailure(problem('client.offline', 'errors.offline', { status: 0 }), SHOWN)).toMatchObject({ message: 'errors.offline' });
    expect(placementFailure(problem('constructor', 'Odd code.'), SHOWN)).toMatchObject({ kind: 'toast', message: 'Odd code.' });
  });

  it('leaves address problems to their inputs but toasts one about a field the page cannot show', () => {
    const invalid = (fieldErrors: Record<string, string[]>) =>
      problem('validation.failed', 'One or more validation errors occurred.', { status: 400, isValidation: true, fieldErrors });

    expect(placementFailure(invalid({ pincode: ['Enter a 6-digit PIN code.'] }), SHOWN)).toEqual({ kind: 'fields' });
    expect(placementFailure(invalid({ landmark: ['Too long.'] }), SHOWN)).toEqual({
      kind: 'toast',
      message: 'checkout.errors.invalid',
      code: 'validation.failed',
    });
  });

  it('stays quiet on a 401, which signs the buyer in again', () => {
    expect(placementFailure(problem('http.401', 'errors.unauthorized', { status: 401 }), SHOWN)).toEqual({ kind: 'none' });
  });
});
