import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  ProductDto,
  apiV1AdminCatalogProductsProductIdPublishPost,
  apiV1AdminCatalogProductsProductIdPut,
  apiV1AdminCatalogSellersGet,
  apiV1AdminInventoryStockProductIdGet,
  apiV1CatalogCategoriesGet,
  catalogGetProduct,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { ProductEditPage } from './product-edit.page';

const product: ProductDto = {
  id: 'p1',
  sku: 'DIYA-12',
  name: 'Cow Dung Diya, pack of 12',
  slug: 'cow-dung-diya-pack-of-12',
  brand: null,
  description: 'Hand made.',
  price: 120,
  currency: 'INR',
  status: 'InReview',
  sellerId: 's1',
  category: { id: 'c1', name: 'Diyas', slug: 'diyas', parentId: null },
  onHandQuantity: 10,
  reservedQuantity: 2,
  createdAtUtc: '2026-09-22T10:00:00Z',
  modifiedAtUtc: null,
  package: null,
  reviewNote: null,
  currentPrice: 120,
  sale: null,
};

function setUp(permissions: readonly string[]) {
  const invoke = vi.fn(async (fn: unknown) => {
    switch (fn) {
      case catalogGetProduct:
        return product;
      case apiV1CatalogCategoriesGet:
        return [product.category];
      case apiV1AdminCatalogSellersGet:
        return [{ id: 's1', shopName: 'UP Gaushala Collective' }];
      case apiV1AdminInventoryStockProductIdGet:
        return { level: { productId: 'p1', onHandQuantity: 10, reservedQuantity: 2, availableQuantity: 8 }, recentMovements: [] };
      case apiV1AdminCatalogProductsProductIdPublishPost:
        return { ...product, status: 'Active' };
      case apiV1AdminCatalogProductsProductIdPut:
        return product;
      default:
        throw new Error('unexpected call');
    }
  });

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideRouter([]),
      provideI18n(),
      { provide: Api, useValue: { invoke } },
      {
        provide: CurrentUserStore,
        useValue: {
          has: (p: string) => permissions.includes(p),
          hasAll: (ps: readonly string[]) => ps.every((p) => permissions.includes(p)),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(ProductEditPage);
  fixture.componentRef.setInput('productId', 'p1');

  return { fixture, invoke };
}

describe('ProductEditPage', () => {
  it('shows a listing in review by seller name, with its stock, and publishes it', async () => {
    const { fixture, invoke } = setUp([
      'catalog.products.read',
      'catalog.products.write',
      'inventory.stock.read',
      'inventory.stock.write',
    ]);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('UP Gaushala Collective');
    expect(text).toContain('10 on hand, 2 held for checkouts.');
    expect(text).toContain('Open the review queue');

    const publish = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: HTMLButtonElement) => b.textContent?.trim() === 'Publish'
    ) as HTMLButtonElement;
    publish.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminCatalogProductsProductIdPublishPost, { productId: 'p1' });
    expect(fixture.nativeElement.textContent).toContain('Live');
  });

  it('is read-only to a caller who can only read the catalogue', async () => {
    const { fixture, invoke } = setUp(['catalog.products.read']);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('You can see this listing but not change it.');
    expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeNull();
    expect((fixture.nativeElement.querySelector('input[name="name"]') as HTMLInputElement).readOnly).toBe(true);
    expect(invoke).not.toHaveBeenCalledWith(apiV1AdminCatalogSellersGet, expect.anything());
    expect(invoke).not.toHaveBeenCalledWith(apiV1AdminInventoryStockProductIdGet, expect.anything());
  });
});
