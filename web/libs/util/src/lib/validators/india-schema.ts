import { pattern } from '@angular/forms/signals';
import { IndiaPatterns } from './india-patterns';

/**
 * Path to a string field inside a Signal Forms schema callback. Derived from `pattern` itself
 * so these helpers keep compiling as the experimental API settles.
 */
type StringFieldPath = Parameters<typeof pattern>[0];

/**
 * Signal Forms counterparts of the ReactiveForms validators. Each takes a field path from a
 * schema callback:
 *
 * ```ts
 * const profile = form(model, (path) => {
 *   required(path.gstin);
 *   gstin(path.gstin);
 * });
 * ```
 *
 * The message is an i18n key, not a sentence, so the template translates it.
 */
export function gstin(path: StringFieldPath): void {
  pattern(path, IndiaPatterns.gstin, { message: 'validation.gstin' });
}

export function pan(path: StringFieldPath): void {
  pattern(path, IndiaPatterns.pan, { message: 'validation.pan' });
}

export function ifsc(path: StringFieldPath): void {
  pattern(path, IndiaPatterns.ifsc, { message: 'validation.ifsc' });
}

export function pincode(path: StringFieldPath): void {
  pattern(path, IndiaPatterns.pincode, { message: 'validation.pincode' });
}

export function mobile(path: StringFieldPath): void {
  pattern(path, IndiaPatterns.mobile, { message: 'validation.mobile' });
}
