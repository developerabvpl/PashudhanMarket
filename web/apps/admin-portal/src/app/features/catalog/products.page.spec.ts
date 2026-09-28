import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  AdminProductSummaryDto,
  Api,
  apiV1AdminCatalogProductsGet,
  apiV1CatalogCategoriesGet,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { ProductsPage } from './products.page';

const diya: AdminProductSummaryDto = {
  id: 'p1',
  sku: 'DIYA-12',
  name: 'Cow Dung Diya, pack of 12',
  price: 120,
  currency: 'INR',
  status: 'Active',
  categoryId: 'c1',
  categoryName: 'Diyas',
  sellerId: 's1',
  sellerName: 'UP Gaushala Collective',
  availableQuantity: 8,
  hasPackage: false,
  updatedAtUtc: '2026-09-22T10:00:00Z',
};

describe('ProductsPage', () => {
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invoke = vi.fn(async (fn: unknown) => {
      if (fn === apiV1AdminCatalogProductsGet) {
        return { items: [diya], page: 1, pageSize: 25, totalCount: 1, hasNextPage: false, totalPages: 1 };
      }

      if (fn === apiV1CatalogCategoriesGet) {
        return [{ id: 'c1', name: 'Diyas', slug: 'diyas', parentId: null }];
      }

      throw new Error('unexpected call');
    });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideI18n(translations),
        { provide: Api, useValue: { invoke } },
        { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
      ],
    });
  });

  it('lists every status by default, naming seller and category and flagging a missing package', async () => {
    const fixture = TestBed.createComponent(ProductsPage);
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminCatalogProductsGet, {
      Page: 1,
      PageSize: 25,
      Search: undefined,
      Status: undefined,
      CategoryId: undefined,
    });

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Cow Dung Diya, pack of 12');
    expect(text).toContain('UP Gaushala Collective');
    expect(text).toContain('Diyas');
    expect(text).toContain('No package');
  });

  it('searches from the first page', async () => {
    const fixture = TestBed.createComponent(ProductsPage);
    await fixture.whenStable();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    (form.querySelector('input[name="search"]') as HTMLInputElement).value = ' diya ';
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenLastCalledWith(apiV1AdminCatalogProductsGet, expect.objectContaining({ Page: 1, Search: 'diya' }));
  });
});
