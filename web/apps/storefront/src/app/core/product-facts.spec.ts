import { describe, expect, it } from 'vitest';
import { productFacts } from './product-facts';

describe('productFacts', () => {
  it('reads a weight and a pack count out of a listing title', () => {
    expect(productFacts('Maa Agarbatti Guggul Cow Dung Agarbatti 1kg (Pack of 10), Low Smoke')).toEqual(
      { size: '1kg', pack: 'Pack of 10', dimension: null }
    );
  });

  it('reads millilitres', () => {
    expect(productFacts('ProKart Original Cow Gomutra Ark Distilled, 100ml').size).toBe('100ml');
  });

  it('normalises litres to a capital L, because "1l" reads as "eleven"', () => {
    expect(productFacts('Patanjali Gonyle Floor Cleaner, 1L').size).toBe('1L');
    expect(productFacts('Patanjali Gonyle Floor Cleaner, 5 l').size).toBe('5L');
  });

  it('counts loose units when the title does not say "pack of"', () => {
    expect(productFacts('Kausthubham Cow Dung Sambrani Cups - Havan Cups (12 Cups)').pack).toBe(
      '12 Cups'
    );
    expect(productFacts('Goseva Gir Cow Gomutra Ghanvati, 120 Tab').pack).toBe('120 Tab');
  });

  it('prefers an explicit "pack of" over a bare count elsewhere in the title', () => {
    const facts = productFacts('Sadrishi Desi Cow Dung Cake (Pack of 17) for Havan Pooja, 490g');

    expect(facts.pack).toBe('Pack of 17');
    expect(facts.size).toBe('490g');
  });

  it('reads a stated physical size', () => {
    expect(productFacts('Inditradition Holy Cow Dung Cake, Round 5 inch (Pack of 24)').dimension).toBe(
      '5 inch'
    );
    expect(
      productFacts('JAINATH ORGANIC Cow Dung Upale, 7.25x7.25 inch, Pack of 6').dimension
    ).toBe('7.25x7.25 inch');
  });

  it('yields nothing rather than a guess when the title states no attributes', () => {
    expect(productFacts('Ugaoo Cow Dung Manure Fertilizer for Plants')).toEqual({
      size: null,
      pack: null,
      dimension: null,
    });
  });
});
