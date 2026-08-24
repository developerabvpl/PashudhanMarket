import { TestBed } from '@angular/core/testing';
import { ProductDto } from '@upbazaar/data-access';
import { beforeEach, describe, expect, it } from 'vitest';
import { CartStore, MAX_QUANTITY } from './cart.store';

function product(overrides: Partial<ProductDto> = {}): ProductDto {
  return {
    id: 'p1',
    sku: 'UPB-AGB-001',
    name: 'Gurushraddha Cow Dung Dhoop Agarbatti',
    slug: 'gurushraddha-cow-dung-dhoop-agarbatti',
    brand: 'Gurushraddha',
    description: null,
    price: 139,
    currency: 'INR',
    status: 'Active',
    sellerId: 's1',
    category: { id: 'c1', name: 'Gobar Agarbatti / Dhoop Batti', slug: 'agb', parentId: null },
    onHandQuantity: 20,
    reservedQuantity: 0,
    createdAtUtc: '2026-08-24T00:00:00Z',
    modifiedAtUtc: null,
    ...overrides,
  };
}

describe('CartStore', () => {
  let cart: CartStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    cart = TestBed.inject(CartStore);
  });

  it('starts empty', () => {
    expect(cart.isEmpty()).toBe(true);
    expect(cart.count()).toBe(0);
    expect(cart.subtotal()).toBe(0);
  });

  it('adds a product and totals it', () => {
    expect(cart.add(product())).toBe(true);

    expect(cart.count()).toBe(1);
    expect(cart.subtotal()).toBe(139);
    expect(cart.lines()[0].sku).toBe('UPB-AGB-001');
  });

  it('tops up the existing line rather than repeating the product', () => {
    cart.add(product());
    cart.add(product(), 2);

    expect(cart.lines()).toHaveLength(1);
    expect(cart.count()).toBe(3);
    expect(cart.subtotal()).toBe(417);
  });

  it('refuses a product with no price, which would total as free', () => {
    expect(cart.add(product({ price: 0 }))).toBe(false);
    expect(cart.isEmpty()).toBe(true);
  });

  it('refuses a non-positive quantity', () => {
    expect(cart.add(product(), 0)).toBe(false);
    expect(cart.add(product(), -3)).toBe(false);
    expect(cart.isEmpty()).toBe(true);
  });

  it('caps a line rather than letting a stuck key run away', () => {
    cart.add(product(), MAX_QUANTITY + 50);

    expect(cart.count()).toBe(MAX_QUANTITY);

    cart.add(product(), 10);

    expect(cart.count()).toBe(MAX_QUANTITY);
  });

  it('removes the line when the quantity reaches zero', () => {
    cart.add(product(), 2);
    cart.setQuantity('p1', 0);

    expect(cart.isEmpty()).toBe(true);
  });

  it('reports how many of one product are held', () => {
    cart.add(product(), 4);

    expect(cart.quantityOf('p1')).toBe(4);
    expect(cart.quantityOf('nothing')).toBe(0);
  });

  it('survives a reload', () => {
    cart.add(product(), 3);
    cart.add(product({ id: 'p2', sku: 'UPB-ARK-005', price: 69 }));

    TestBed.resetTestingModule();
    const reloaded = TestBed.inject(CartStore);

    expect(reloaded.count()).toBe(4);
    expect(reloaded.subtotal()).toBe(3 * 139 + 69);
  });

  it('clears the stored basket rather than leaving an empty array behind', () => {
    cart.add(product());
    cart.clear();

    expect(cart.isEmpty()).toBe(true);
    expect(localStorage.getItem('upb.cart')).toBeNull();
  });

  it('drops stored lines that are shaped wrong instead of totalling NaN', () => {
    localStorage.setItem(
      'upb.cart',
      JSON.stringify([
        { productId: 'good', sku: 'S', name: 'N', price: 100, currency: 'INR', quantity: 2 },
        { productId: 'no price', sku: 'S', name: 'N', currency: 'INR', quantity: 1 },
        { productId: 'zero qty', sku: 'S', name: 'N', price: 5, currency: 'INR', quantity: 0 },
      ])
    );

    TestBed.resetTestingModule();
    const reloaded = TestBed.inject(CartStore);

    expect(reloaded.lines()).toHaveLength(1);
    expect(reloaded.subtotal()).toBe(200);
  });

  it('ignores stored junk', () => {
    localStorage.setItem('upb.cart', 'not json');

    TestBed.resetTestingModule();

    expect(TestBed.inject(CartStore).isEmpty()).toBe(true);
  });
});
