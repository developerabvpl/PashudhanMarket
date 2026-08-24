import { describe, expect, it } from 'vitest';
import { categoryPhotoUrl } from './product-thumb';

describe('categoryPhotoUrl', () => {
  it('maps a SKU to its category photograph', () => {
    expect(categoryPhotoUrl('UPB-AGB-001')).toBe('/media/products/agb.jpg');
    expect(categoryPhotoUrl('UPB-KAN-008')).toBe('/media/products/kan.jpg');
  });

  it('returns nothing for a category with no photograph, so nothing 404s', () => {
    expect(categoryPhotoUrl('UPB-GHN-002')).toBeNull();
  });

  it('returns nothing for a SKU it cannot read', () => {
    expect(categoryPhotoUrl('nonsense')).toBeNull();
    expect(categoryPhotoUrl('')).toBeNull();
  });
});
