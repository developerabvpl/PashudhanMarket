import { ApiProblem } from '@upbazaar/data-access';

/** Why a coupon took nothing off, as words for the buyer's language. */
export interface CouponError {
  /** Translation key. */
  readonly key: string;
  /** The order minimum, for a coupon the basket does not reach; null otherwise. */
  readonly minimum: number | null;
}

/**
 * Each reason the API gives for refusing a coupon at checkout, by its stable code. The API's
 * title is English only, so the page shows its own words for each code it knows and falls back
 * to the title (or the generic error) for anything new.
 */
const KEYS: Readonly<Record<string, string>> = {
  'promotions.coupon.unknown': 'checkout.coupon.errors.unknown',
  'promotions.coupon.not_valid_now': 'checkout.coupon.errors.notValidNow',
  'promotions.coupon.used_up': 'checkout.coupon.errors.usedUp',
  'promotions.coupon.already_used': 'checkout.coupon.errors.alreadyUsed',
  'promotions.coupon.nothing_covered': 'checkout.coupon.errors.nothingCovered',
  'promotions.coupon.delivery_already_free': 'checkout.coupon.errors.deliveryAlreadyFree',
  'promotions.coupon.below_minimum': 'checkout.coupon.errors.belowMinimum',
  'promotions.concurrent_change': 'checkout.coupon.errors.concurrentChange',
};

/**
 * The minimum travels only inside the API's English title ("...at least Rs 499 of the goods it
 * covers."), so it is read back from there; if the wording ever changes the buyer still gets the
 * reason, just without the amount.
 */
const MINIMUM = /Rs\s*([0-9]+(?:\.[0-9]+)?)/;

export function couponError(problem: ApiProblem): CouponError {
  if (problem.code === 'promotions.coupon.below_minimum') {
    const match = MINIMUM.exec(problem.title);

    return match
      ? { key: 'checkout.coupon.errors.belowMinimum', minimum: Number(match[1]) }
      : { key: 'checkout.coupon.errors.belowMinimumUnknown', minimum: null };
  }

  return { key: KEYS[problem.code] ?? problem.title, minimum: null };
}
