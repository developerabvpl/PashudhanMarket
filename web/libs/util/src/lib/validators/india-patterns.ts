/**
 * Format rules for the Indian identifiers UP Bazaar collects from sellers and buyers.
 *
 * These check *shape*, not existence: a well-formed GSTIN can still be unregistered, so the
 * API remains the authority. Catching the typo in the browser just saves a round trip.
 */
export const IndiaPatterns = {
  /**
   * 15 characters: 2-digit state code, 10-character PAN, 1 entity digit, "Z", 1 checksum.
   */
  gstin: /^[0-3][0-9][A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/,

  /** 5 letters, 4 digits, 1 letter. The fourth letter encodes the holder type. */
  pan: /^[A-Z]{5}[0-9]{4}[A-Z]$/,

  /** 4-letter bank code, "0", then a 6-character branch code. */
  ifsc: /^[A-Z]{4}0[A-Z0-9]{6}$/,

  /** 6 digits, never starting with zero. */
  pincode: /^[1-9][0-9]{5}$/,

  /** 10 digits starting 6-9, the range Indian mobile numbers are allocated from. */
  mobile: /^[6-9][0-9]{9}$/,
} as const;

export type IndiaFormat = keyof typeof IndiaPatterns;

/** Uppercases and strips spacing so "  22aaaaa0000a1z5 " still validates. */
export function normalizeIdentifier(value: string): string {
  return value.replace(/\s+/g, '').toUpperCase();
}

/** Keeps only digits, so "+91 98765 43210" and "098765 43210" both reduce to 10 digits. */
export function normalizeMobile(value: string): string {
  const digits = value.replace(/\D/g, '');

  if (digits.length === 12 && digits.startsWith('91')) {
    return digits.slice(2);
  }

  if (digits.length === 11 && digits.startsWith('0')) {
    return digits.slice(1);
  }

  return digits;
}

export function isGstin(value: string): boolean {
  return IndiaPatterns.gstin.test(normalizeIdentifier(value));
}

export function isPan(value: string): boolean {
  return IndiaPatterns.pan.test(normalizeIdentifier(value));
}

export function isIfsc(value: string): boolean {
  return IndiaPatterns.ifsc.test(normalizeIdentifier(value));
}

export function isPincode(value: string): boolean {
  return IndiaPatterns.pincode.test(value.trim());
}

export function isMobile(value: string): boolean {
  return IndiaPatterns.mobile.test(normalizeMobile(value));
}

/**
 * A GSTIN embeds the holder's PAN in characters 3-12, so the two must agree when a seller
 * supplies both.
 */
export function panMatchesGstin(pan: string, gstin: string): boolean {
  return normalizeIdentifier(gstin).slice(2, 12) === normalizeIdentifier(pan);
}
