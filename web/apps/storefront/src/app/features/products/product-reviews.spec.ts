import { TestBed } from '@angular/core/testing';
import { PLATFORM_ID, provideZonelessChangeDetection } from '@angular/core';
import { Api, ProductReviewsDto, PublicReviewDto, apiV1ReviewsProductsProductIdGet } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { ProductReviews } from './product-reviews';

function review(id: string, overrides: Partial<PublicReviewDto> = {}): PublicReviewDto {
  return {
    id,
    reviewerName: 'Asha Devi',
    rating: 5,
    title: null,
    body: null,
    photos: [],
    reply: null,
    createdAtUtc: '2026-09-24T10:00:00Z',
    ...overrides,
  };
}

function page(items: PublicReviewDto[], pageNumber: number, totalCount: number): ProductReviewsDto {
  return {
    productId: 'p1',
    summary: { average: 4.5, count: totalCount, stars: [0, 0, 0, totalCount - 1, 1] },
    items,
    page: pageNumber,
    pageSize: 10,
    totalCount,
  };
}

async function render(invoke: ReturnType<typeof vi.fn>, platform = 'browser') {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(),
      { provide: Api, useValue: { invoke } },
      { provide: PLATFORM_ID, useValue: platform },
    ],
  });

  const fixture = TestBed.createComponent(ProductReviews);
  fixture.componentRef.setInput('productId', 'p1');
  await fixture.whenStable();

  return { fixture, element: fixture.nativeElement as HTMLElement };
}

describe('ProductReviews', () => {
  it('shows the rating, approved words and the seller reply, and loads more on request', async () => {
    const invoke = vi.fn(async (_fn: unknown, params: { page: number }) =>
      params.page === 1
        ? page(
            [
              review('r1', { title: 'Burns clean', body: 'No smoke.', reply: { text: 'Thank you!', repliedAtUtc: '2026-09-24T11:00:00Z', hidden: false } }),
              review('r2', { rating: 4 }),
            ],
            1,
            3
          )
        : page([review('r3', { rating: 4 })], 2, 3)
    );

    const { fixture, element } = await render(invoke);

    expect(invoke).toHaveBeenCalledWith(apiV1ReviewsProductsProductIdGet, { productId: 'p1', page: 1, pageSize: 10 });
    expect(element.textContent).toContain('4.5');
    expect(element.textContent).toContain('3 ratings');
    expect(element.textContent).toContain('Burns clean');
    expect(element.textContent).toContain('Reply from the seller');
    expect(element.querySelectorAll('li.py-5').length).toBe(2);

    const more = [...element.querySelectorAll('button')].find((b) => b.textContent?.includes('Show more reviews'))!;
    more.click();
    await fixture.whenStable();

    expect(element.querySelectorAll('li.py-5').length).toBe(3);
    expect([...element.querySelectorAll('button')].some((b) => b.textContent?.includes('Show more reviews'))).toBe(false);
  });

  it('says so when nobody has rated the product', async () => {
    const { element } = await render(vi.fn(async () => page([], 1, 0)));

    expect(element.textContent).toContain('No ratings yet.');
  });

  it('leaves reviews out of the prerendered page', async () => {
    const invoke = vi.fn();
    await render(invoke, 'server');

    expect(invoke).not.toHaveBeenCalled();
  });
});
