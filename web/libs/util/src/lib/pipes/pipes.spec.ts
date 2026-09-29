import { DateIstPipe } from './date-ist.pipe';
import { InrCurrencyPipe } from './inr-currency.pipe';

describe('InrCurrencyPipe', () => {
  const pipe = new InrCurrencyPipe();

  it('groups digits the Indian way, not in thousands', () => {
    expect(pipe.transform(123456.78)).toBe('₹1,23,456.78');
  });

  it('renders the code instead of the symbol on request', () => {
    expect(pipe.transform(4599, 'code')).toBe('INR 4,599.00');
  });

  it('omits the symbol entirely when asked', () => {
    expect(pipe.transform(4599, 'none')).toBe('4,599.00');
  });

  it('accepts a numeric string, because the API sends decimals as numbers or strings', () => {
    expect(pipe.transform('99.5')).toBe('₹99.50');
  });

  it.each([null, undefined, '', 'not-a-number'])('renders nothing for %s', (value) => {
    expect(pipe.transform(value as never)).toBe('');
  });
});

describe('DateIstPipe', () => {
  const pipe = new DateIstPipe();

  it('renders a UTC instant in IST, not the runner timezone', () => {
    // 18:30 UTC is 00:00 IST the next day: the date must roll over.
    expect(pipe.transform('2026-03-14T18:30:00Z')).toBe('15 Mar 2026');
  });

  it('includes the time when asked', () => {
    expect(pipe.transform('2026-03-14T18:30:00Z', 'datetime')).toContain('12:00 am');
  });

  it.each([null, undefined, '', 'nonsense'])('renders nothing for %s', (value) => {
    expect(pipe.transform(value as never)).toBe('');
  });
});

describe('InrCurrencyPipe auto decimals', () => {
  const pipe = new InrCurrencyPipe();

  it('drops the paise on a whole rupee amount, the way a shelf price reads', () => {
    expect(pipe.transform(139, 'symbol', 'auto')).toBe('₹139');
    expect(pipe.transform(123456, 'symbol', 'auto')).toBe('₹1,23,456');
  });

  it('keeps them the moment there are any', () => {
    expect(pipe.transform(139.5, 'symbol', 'auto')).toBe('₹139.50');
  });

  it('still defaults to two decimals, which is what an order total wants', () => {
    expect(pipe.transform(139)).toBe('₹139.00');
  });
});

describe('InrCurrencyPipe negative amounts', () => {
  const pipe = new InrCurrencyPipe();
  const minus = String.fromCharCode(0x2212);

  it('puts a true minus sign before the symbol, the way the storefront shows a discount', () => {
    expect(pipe.transform(-800)).toBe(`${minus}₹800.00`);
    expect(pipe.transform(-800)).not.toContain('-');
  });

  it('keeps the Indian grouping and the chosen decimals on a negative amount', () => {
    expect(pipe.transform(-123456.78)).toBe(`${minus}₹1,23,456.78`);
    expect(pipe.transform(-50, 'symbol', 'auto')).toBe(`${minus}₹50`);
    expect(pipe.transform('-99.5')).toBe(`${minus}₹99.50`);
  });

  it('signs the code and bare forms too', () => {
    expect(pipe.transform(-4599, 'code')).toBe(`${minus}INR 4,599.00`);
    expect(pipe.transform(-4599, 'none')).toBe(`${minus}4,599.00`);
  });

  it('shows no sign on an amount that rounds to zero', () => {
    expect(pipe.transform(-0)).toBe('₹0.00');
    expect(pipe.transform(-0.001)).toBe('₹0.00');
  });
});
