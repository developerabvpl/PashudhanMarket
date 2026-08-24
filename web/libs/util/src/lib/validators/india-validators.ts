import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import {
  isGstin,
  isIfsc,
  isMobile,
  isPan,
  isPincode,
  panMatchesGstin,
} from './india-patterns';

/**
 * ReactiveForms validators, for the screens that use typed ReactiveForms.
 * Signal Forms screens use the schema helpers in `india-schema.ts` instead.
 *
 * Each error key doubles as an i18n key under `validation.*`, so a template can render the
 * message without a switch statement per field.
 */
function formatValidator(key: string, test: (value: string) => boolean): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;

    // Emptiness is Validators.required's job; an optional field stays valid when blank.
    if (value === null || value === undefined || value === '') {
      return null;
    }

    return test(String(value)) ? null : { [key]: true };
  };
}

export const gstinValidator = formatValidator('gstin', isGstin);
export const panValidator = formatValidator('pan', isPan);
export const ifscValidator = formatValidator('ifsc', isIfsc);
export const pincodeValidator = formatValidator('pincode', isPincode);
export const mobileValidator = formatValidator('mobile', isMobile);

/**
 * Cross-field check for the seller onboarding form: the PAN inside the GSTIN must be the PAN
 * the seller typed. Apply to the group holding both controls.
 */
export function panGstinConsistencyValidator(
  panControlName = 'pan',
  gstinControlName = 'gstin'
): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const pan = group.get(panControlName)?.value;
    const gstin = group.get(gstinControlName)?.value;

    if (!pan || !gstin) {
      return null;
    }

    return panMatchesGstin(String(pan), String(gstin)) ? null : { panGstinMismatch: true };
  };
}
