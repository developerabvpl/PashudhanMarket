import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { CurrentUserStore } from '@upbazaar/auth';
import { Api, apiV1AdminCatalogProductsGet } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { ListingReviewPage } from './listing-review.page';

describe('ListingReviewPage', () => {
  it('asks for listings in review, oldest first, and names each seller', async () => {
    const invoke = vi.fn(async () => ({
      items: [
        {
          id: 'p1',
          sku: 'DIYA-12',
          name: 'Cow Dung Diya, pack of 12',
          price: 120,
          currency: 'INR',
          status: 'InReview',
          categoryId: 'c1',
          categoryName: 'Diyas',
          sellerId: 's1',
          sellerName: 'UP Gaushala Collective',
          availableQuantity: 8,
          hasPackage: true,
          updatedAtUtc: '2026-09-22T10:00:00Z',
        },
      ],
      page: 1,
      pageSize: 100,
      totalCount: 1,
      hasNextPage: false,
      totalPages: 1,
    }));

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideI18n(),
        { provide: Api, useValue: { invoke } },
        { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
      ],
    });

    const fixture = TestBed.createComponent(ListingReviewPage);
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminCatalogProductsGet, { Status: 'InReview', OldestFirst: true, PageSize: 100 });
    expect(fixture.nativeElement.textContent).toContain('UP Gaushala Collective');
  });
});
