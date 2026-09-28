import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, ReviewDto, apiV1SellerReviewsGet, apiV1SellerReviewsReviewIdReplyPut } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { ReviewsPage } from './reviews.page';

function review(id: string, overrides: Partial<ReviewDto> = {}): ReviewDto {
  return {
    id,
    productId: 'p1',
    productName: 'Gobar Diya',
    sellerId: 's1',
    reviewerName: 'Asha Devi',
    rating: 3,
    title: null,
    body: null,
    contentStatus: 'Pending',
    moderationNote: null,
    photos: [],
    reply: null,
    createdAtUtc: '2026-09-24T10:00:00Z',
    contentSubmittedAtUtc: '2026-09-24T10:00:00Z',
    ...overrides,
  };
}

async function render(items: ReviewDto[]) {
  const invoke = vi.fn(async (fn: unknown, params: { body?: { text: string } }) => {
    switch (fn) {
      case apiV1SellerReviewsGet:
        return { items, page: 1, pageSize: 25, totalCount: items.length };
      case apiV1SellerReviewsReviewIdReplyPut:
        return { ...items[0], reply: { text: params.body!.text, repliedAtUtc: '2026-09-24T12:00:00Z', hidden: false } };
      default:
        throw new Error('unexpected call');
    }
  });

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(translations), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(ReviewsPage);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

describe('ReviewsPage', () => {
  it('shows stars at once and says the words wait for approval', async () => {
    const { element } = await render([review('r1'), review('r2', { contentStatus: 'Approved', body: 'Lovely diyas.' })]);

    expect(element.textContent).toContain("The buyer's words are waiting for our approval.");
    expect(element.textContent).toContain('Lovely diyas.');
  });

  it('posts a trimmed reply and shows it in place', async () => {
    const { fixture, invoke, element } = await render([review('r1')]);

    const text = element.querySelector('textarea') as HTMLTextAreaElement;
    text.value = '  Each diya is 7 cm across.  ';
    text.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    const post = [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Post reply') as HTMLButtonElement;
    post.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerReviewsReviewIdReplyPut, { reviewId: 'r1', body: { text: 'Each diya is 7 cm across.' } });
    expect([...element.querySelectorAll('button')].some((b) => b.textContent?.trim() === 'Update reply')).toBe(true);
  });

  it('tells the seller when staff have hidden their reply', async () => {
    const { element } = await render([
      review('r1', { reply: { text: 'Rude words.', repliedAtUtc: '2026-09-24T12:00:00Z', hidden: true } }),
    ]);

    expect(element.textContent).toContain('Our staff have hidden this reply from the storefront.');
  });
});
