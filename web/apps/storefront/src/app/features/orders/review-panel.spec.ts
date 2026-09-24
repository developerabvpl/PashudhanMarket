import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import {
  Api,
  MyReviewDto,
  ReviewDto,
  apiV1ReviewsMineProductIdGet,
  apiV1ReviewsMineProductIdPhotosPost,
  apiV1ReviewsMineProductIdPut,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { ReviewPanel } from './review-panel';

const review: ReviewDto = {
  id: 'r1',
  productId: 'p1',
  productName: 'Gobar Diya',
  sellerId: 's1',
  reviewerName: 'Asha Devi',
  rating: 4,
  title: 'Burns clean',
  body: 'No smoke at all.',
  contentStatus: 'Pending',
  moderationNote: null,
  photos: [],
  reply: null,
  createdAtUtc: '2026-09-24T10:00:00Z',
  contentSubmittedAtUtc: '2026-09-24T10:00:00Z',
};

function mine(overrides: Partial<MyReviewDto> = {}): MyReviewDto {
  return { productId: 'p1', canReview: true, photosEnabled: true, maxPhotos: 3, review: null, ...overrides };
}

async function render(state: MyReviewDto, onCall: (fn: unknown, params: unknown) => unknown = () => review) {
  const invoke = vi.fn(async (fn: unknown, params: unknown) => (fn === apiV1ReviewsMineProductIdGet ? state : onCall(fn, params)));

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(ReviewPanel);
  fixture.componentRef.setInput('productId', 'p1');
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

function button(element: HTMLElement, text: string): HTMLButtonElement {
  return [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
}

describe('ReviewPanel', () => {
  it('shows nothing for a product the buyer may not review', async () => {
    const { element } = await render(mine({ canReview: false }));

    expect(element.textContent?.trim()).toBe('');
  });

  it('insists on stars, then saves the rating and words', async () => {
    const { fixture, invoke, element } = await render(mine({ photosEnabled: false }));

    button(element, 'Rate and review this product').click();
    await fixture.whenStable();

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(element.textContent).toContain('Choose from one to five stars.');
    expect(invoke).toHaveBeenCalledTimes(1);

    (element.querySelector('[aria-label="4 out of 5 stars"][role="radio"]') as HTMLButtonElement).click();
    const body = element.querySelector('textarea') as HTMLTextAreaElement;
    body.value = '  No smoke at all.  ';
    body.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1ReviewsMineProductIdPut, {
      productId: 'p1',
      body: { rating: 4, title: null, body: 'No smoke at all.' },
    });
    expect(element.textContent).toContain('Your words and photos will appear once we have checked them.');
  });

  it('tells the buyer why their words were not published', async () => {
    const { element } = await render(
      mine({ review: { ...review, contentStatus: 'Rejected', moderationNote: 'Please leave out phone numbers.' } })
    );

    expect(element.textContent).toContain('your words and photos are not shown: Please leave out phone numbers.');
  });

  it('refuses a photo over 5 MB without uploading it, and uploads one under', async () => {
    const { fixture, invoke, element } = await render(mine({ review }));

    button(element, 'Edit review').click();
    await fixture.whenStable();

    const input = element.querySelector('input[type="file"]') as HTMLInputElement;
    const choose = async (file: File) => {
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change'));
      await fixture.whenStable();
    };

    await choose(new File([new Uint8Array(6 * 1024 * 1024)], 'huge.jpg', { type: 'image/jpeg' }));
    expect(element.textContent).toContain('That photo is over 5 MB.');
    expect(invoke).not.toHaveBeenCalledWith(apiV1ReviewsMineProductIdPhotosPost, expect.anything());

    const small = new File([new Uint8Array(10)], 'diya.png', { type: 'image/png' });
    await choose(small);
    expect(invoke).toHaveBeenCalledWith(apiV1ReviewsMineProductIdPhotosPost, { productId: 'p1', body: { file: small } });
  });

  it('offers no photos where the store is switched off', async () => {
    const { fixture, element } = await render(mine({ review, photosEnabled: false }));

    button(element, 'Edit review').click();
    await fixture.whenStable();

    expect(element.querySelector('input[type="file"]')).toBeNull();
  });
});
