import { describe, expect, it } from 'vitest';
import { productPhotoUrl } from './product-thumb';

describe('productPhotoUrl', () => {
  it('falls back to the category photograph', () => {
    expect(productPhotoUrl('UPB-KHD-004', 'PUNAMULCH Cow Dung Manure 20kg')).toBe(
      '/media/products/Khad.jpg'
    );
  });

  it('percent-encodes a supplied filename that contains spaces', () => {
    expect(productPhotoUrl('UPB-SAM-001', 'Kausthubham Cow Dung Sambrani Cups')).toBe(
      '/media/products/haven%20cup.jpg'
    );
    expect(productPhotoUrl('UPB-KAN-002', 'Sadrishi Desi Cow Dung Cake')).toBe(
      '/media/products/Cow%20dung%20cake.jpg'
    );
  });

  it('gives a bamboo-cored agarbatti its own picture, not the dhoop one', () => {
    expect(productPhotoUrl('UPB-AGB-002', 'IRASVA Cow Dung Agarbatti Organic (Loban, 250g)')).toBe(
      '/media/products/agarbatti.jpg'
    );
    expect(productPhotoUrl('UPB-AGB-006', 'Maa Agarbatti 5-Aroma Cow Dung Incense Sticks 1kg')).toBe(
      '/media/products/agarbatti.jpg'
    );
  });

  it('treats a listing that says both as a dhoop, which is the category default', () => {
    expect(
      productPhotoUrl('UPB-AGB-001', 'Gurushraddha Cow Dung Dhoop Agarbatti, Gau Guggal')
    ).toBe('/media/products/Dhoop.webp');
    expect(productPhotoUrl('UPB-AGB-007', 'Maa Agarbatti Gulab Cow Dung Dhup Stick 1kg')).toBe(
      '/media/products/Dhoop.webp'
    );
  });

  it('returns nothing for a category with no photograph, so nothing is requested', () => {
    expect(productPhotoUrl('UPB-GHN-002', 'Goseva Gir Cow Gomutra Ghanvati')).toBeNull();
  });

  it('returns nothing for a SKU it cannot read', () => {
    expect(productPhotoUrl('nonsense')).toBeNull();
    expect(productPhotoUrl('')).toBeNull();
  });
});
