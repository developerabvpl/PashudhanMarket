import {
  isGstin,
  isIfsc,
  isMobile,
  isPan,
  isPincode,
  normalizeMobile,
  panMatchesGstin,
} from './india-patterns';

describe('India identifier formats', () => {
  describe('GSTIN', () => {
    it.each(['09ABCDE1234F1Z5', '27AAPFU0939F1ZV'])('accepts %s', (value) => {
      expect(isGstin(value)).toBe(true);
    });

    it('accepts lowercase and stray spacing', () => {
      expect(isGstin('  09abcde1234f1z5 ')).toBe(true);
    });

    it.each([
      ['too short', '09ABCDE1234F1Z'],
      ['state code out of range', '99ABCDE1234F1Z5'],
      ['missing the fixed Z', '09ABCDE1234F1X5'],
      ['digits where the PAN letters go', '09123451234F1Z5'],
    ])('rejects %s', (_case, value) => {
      expect(isGstin(value)).toBe(false);
    });
  });

  describe('PAN', () => {
    it('accepts a well-formed PAN', () => {
      expect(isPan('ABCDE1234F')).toBe(true);
    });

    it.each(['ABCD1234F', 'ABCDE12345', '12345ABCDE'])('rejects %s', (value) => {
      expect(isPan(value)).toBe(false);
    });
  });

  describe('IFSC', () => {
    it('accepts a well-formed code', () => {
      expect(isIfsc('SBIN0001234')).toBe(true);
    });

    it('rejects a code without the fixed zero in position five', () => {
      expect(isIfsc('SBIN1001234')).toBe(false);
    });
  });

  describe('PIN code', () => {
    it('accepts a six-digit code', () => {
      expect(isPincode('221001')).toBe(true);
    });

    it.each(['021001', '22100', '2210011'])('rejects %s', (value) => {
      expect(isPincode(value)).toBe(false);
    });
  });

  describe('mobile', () => {
    it.each(['9876543210', '+91 98765 43210', '098765 43210'])('accepts %s', (value) => {
      expect(isMobile(value)).toBe(true);
    });

    it.each(['5876543210', '98765', '12345678901234'])('rejects %s', (value) => {
      expect(isMobile(value)).toBe(false);
    });

    it('strips the country code and the trunk zero', () => {
      expect(normalizeMobile('+919876543210')).toBe('9876543210');
      expect(normalizeMobile('09876543210')).toBe('9876543210');
    });
  });

  describe('PAN inside GSTIN', () => {
    it('matches when the GSTIN carries the same PAN', () => {
      expect(panMatchesGstin('ABCDE1234F', '09ABCDE1234F1Z5')).toBe(true);
    });

    it('does not match a different PAN', () => {
      expect(panMatchesGstin('ZZZZZ9999Z', '09ABCDE1234F1Z5')).toBe(false);
    });
  });
});
