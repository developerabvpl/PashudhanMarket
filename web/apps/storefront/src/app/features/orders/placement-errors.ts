import { ApiProblem } from '@upbazaar/data-access';
import { CouponError, couponError, isCouponProblem } from './coupon-errors';

/**
 * Where the checkout shows why an order was not placed. The order is sent with callerShowsErrors,
 * so the app-wide toast stays quiet and the page owns every failure - each one shown once, in the
 * buyer's language wherever the API's code is known:
 * - `coupon`: under the coupon box, as when the code was applied;
 * - `fields`: under the address inputs the API named;
 * - `toast`: everything else, in a toast like the one the interceptor would have raised;
 * - `none`: a 401, which the auth interceptor handles by signing the buyer in again.
 */
export type PlacementFailure =
  | { readonly kind: 'coupon'; readonly error: CouponError }
  | { readonly kind: 'fields' }
  | { readonly kind: 'toast'; readonly message: string; readonly code: string }
  | { readonly kind: 'none' };

/** Toast wording for the placement failures a buyer can actually meet, by the API's code. */
const TOAST_KEYS: Readonly<Record<string, string>> = {
  'cart.empty': 'checkout.errors.cartEmpty',
  'cart.not_ready_for_checkout': 'checkout.errors.cartChanged',
  'cart.product.not_on_sale': 'checkout.errors.cartChanged',
  'cart.product.insufficient_stock': 'checkout.errors.cartChanged',
  'orders.out_of_stock': 'checkout.errors.outOfStock',
  'cart.concurrent_change': 'checkout.errors.concurrentChange',
  'orders.concurrent_change': 'checkout.errors.concurrentChange',
};

/**
 * Sorts a placement failure. `shownFields` are the inputs the page can put a message under; a
 * validation failure naming anything else would otherwise vanish, so it gets a toast too.
 */
export function placementFailure(problem: ApiProblem, shownFields: readonly string[]): PlacementFailure {
  if (problem.status === 401) {
    return { kind: 'none' };
  }

  if (isCouponProblem(problem)) {
    return { kind: 'coupon', error: couponError(problem) };
  }

  if (problem.isValidation) {
    const unshown = Object.keys(problem.fieldErrors).some((field) => !shownFields.includes(field));

    return unshown ? { kind: 'toast', message: 'checkout.errors.invalid', code: problem.code } : { kind: 'fields' };
  }

  return {
    kind: 'toast',
    message: Object.hasOwn(TOAST_KEYS, problem.code) ? TOAST_KEYS[problem.code] : problem.title,
    code: problem.code,
  };
}
