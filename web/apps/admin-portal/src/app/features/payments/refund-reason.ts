import { RefundDto } from '@upbazaar/data-access';

/**
 * The reason codes the API writes on a refund, each with a label under `payments.reasons.`.
 * Listed rather than trusted, so a code added on the server before its label lands here shows the
 * stored English sentence instead of a raw translation key.
 */
const KNOWN_REASONS = new Set(['OrderCancelled', 'PartCancelled', 'Undelivered', 'BuyerReturn', 'PaymentRefused']);

/**
 * The translation key for why a refund is owed, or null when the page should show the sentence
 * stored with it: a refund recorded before codes existed whose sentence matched none of them.
 */
export function refundReasonKey(refund: Pick<RefundDto, 'reasonCode'>): string | null {
  return refund.reasonCode && KNOWN_REASONS.has(refund.reasonCode) ? 'payments.reasons.' + refund.reasonCode : null;
}
