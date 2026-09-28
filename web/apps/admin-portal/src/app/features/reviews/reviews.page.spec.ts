import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  ReviewDto,
  apiV1AdminReviewsGet,
  apiV1AdminReviewsReviewIdApprovePost,
  apiV1AdminReviewsReviewIdRejectPost,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { ReviewsPage } from './reviews.page';

const pending: ReviewDto = {
  id: 'r1',
  productId: 'p1',
  productName: 'Gobar Diya',
  sellerId: 's1',
  reviewerName: 'Asha Devi',
  rating: 1,
  title: null,
  body: 'Call me on 9876543210 for a better deal',
  contentStatus: 'Pending',
  moderationNote: null,
  photos: [{ id: 'ph1', url: '/api/v1/reviews/photos/ph1?expires=1&signature=abc' }],
  reply: null,
  createdAtUtc: '2026-09-24T10:00:00Z',
  contentSubmittedAtUtc: '2026-09-24T10:00:00Z',
};

async function render(canModerate: boolean) {
  const invoke = vi.fn(async (fn: unknown) =>
    fn === apiV1AdminReviewsGet ? { items: [pending], page: 1, pageSize: 25, totalCount: 1 } : { ...pending, contentStatus: 'Approved' }
  );

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(translations),
      { provide: Api, useValue: { invoke } },
      { provide: CurrentUserStore, useValue: { has: () => canModerate, hasAll: () => canModerate } },
    ],
  });

  const fixture = TestBed.createComponent(ReviewsPage);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

function button(element: HTMLElement, text: string): HTMLButtonElement | undefined {
  return [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === text);
}

describe('ReviewsPage', () => {
  it('opens on the queue and shows the first review with its private photo', async () => {
    const { invoke, element } = await render(true);

    expect(invoke).toHaveBeenCalledWith(apiV1AdminReviewsGet, { Status: 'Pending', Page: 1, PageSize: 25 });
    expect(element.textContent).toContain('Call me on 9876543210');
    expect(element.querySelector('img')?.getAttribute('src')).toContain('signature=abc');
  });

  it('rejects only with a note, and approves', async () => {
    const { fixture, invoke, element } = await render(true);

    expect(button(element, 'Reject')!.disabled).toBe(true);

    const note = element.querySelector('textarea') as HTMLTextAreaElement;
    note.value = ' Please leave out phone numbers. ';
    note.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    button(element, 'Reject')!.click();
    await fixture.whenStable();
    expect(invoke).toHaveBeenCalledWith(apiV1AdminReviewsReviewIdRejectPost, {
      reviewId: 'r1',
      body: { note: 'Please leave out phone numbers.' },
    });

    button(element, 'Approve')!.click();
    await fixture.whenStable();
    expect(invoke).toHaveBeenCalledWith(apiV1AdminReviewsReviewIdApprovePost, { reviewId: 'r1' });
  });

  it('shows staff who may only read the review without the buttons', async () => {
    const { element } = await render(false);

    expect(element.textContent).toContain('Call me on 9876543210');
    expect(button(element, 'Approve')).toBeUndefined();
  });
});
