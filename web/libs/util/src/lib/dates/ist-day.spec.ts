import { endOfIstDay, startOfIstDay } from './ist-day';

describe('IST days', () => {
  it('runs from midnight to the last second in India', () => {
    expect(startOfIstDay('2026-10-31')).toBe('2026-10-30T18:30:00.000Z');
    expect(endOfIstDay('2026-10-31')).toBe('2026-10-31T18:29:59.000Z');
  });
});
