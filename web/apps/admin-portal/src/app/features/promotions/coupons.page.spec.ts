import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  CouponDto,
  apiV1AdminPromotionsCouponsCouponIdEndPost,
  apiV1AdminPromotionsCouponsGet,
  apiV1AdminPromotionsCouponsPost,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { CouponsPage, endOfIstDay } from './coupons.page';

const campaign: CouponDto = {
  id: 'c1',
  code: 'DIWALI',
  description: 'Diwali campaign',
  sellerId: null,
  fundedBy: 'Seller',
  discountType: 'Percent',
  value: 10,
  maxDiscount: 200,
  minOrderValue: null,
  startsAtUtc: '2026-09-25T00:00:00Z',
  endsAtUtc: null,
  totalLimit: null,
  perBuyerLimit: 1,
  uses: 3,
  isActive: true,
  sellersJoined: 2,
  joined: false,
};

async function render(canWrite: boolean) {
  const invoke = vi.fn(async (fn: unknown) => (fn === apiV1AdminPromotionsCouponsGet ? [campaign] : campaign));

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(),
      { provide: Api, useValue: { invoke } },
      { provide: CurrentUserStore, useValue: { has: () => canWrite, hasAll: () => canWrite } },
    ],
  });

  const fixture = TestBed.createComponent(CouponsPage);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

function fill(element: HTMLElement, name: string, value: string): void {
  const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('CouponsPage', () => {
  it('lists coupons with who pays and how many sellers joined', async () => {
    const { element } = await render(true);

    expect(element.textContent).toContain('DIWALI');
    expect(element.textContent).toContain('10% off');
    expect(element.textContent).toContain('2 sellers joined');
  });

  it('creates a coupon ending at the close of the chosen day in India', async () => {
    const { fixture, invoke, element } = await render(true);

    fill(element, 'code', ' welcome10 ');
    fill(element, 'description', 'Welcome offer');
    fill(element, 'value', '10');
    fill(element, 'maxDiscount', '100');
    fill(element, 'endsOn', '2026-10-31');
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminPromotionsCouponsPost, {
      body: {
        code: 'welcome10',
        description: 'Welcome offer',
        discountType: 'Percent',
        value: 10,
        fundedBy: 'Platform',
        maxDiscount: 100,
        minOrderValue: null,
        endsAtUtc: '2026-10-31T18:29:59.000Z',
        totalLimit: null,
        perBuyerLimit: 1,
      },
    });
    expect(endOfIstDay('2026-10-31')).toBe('2026-10-31T18:29:59.000Z');
  });

  it('ends a coupon', async () => {
    const { fixture, invoke, element } = await render(true);

    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'End')!.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminPromotionsCouponsCouponIdEndPost, { couponId: 'c1' });
  });

  it('offers no changes to staff who may only read', async () => {
    const { element } = await render(false);

    expect(element.querySelector('form')).toBeNull();
    expect([...element.querySelectorAll('button')].some((b) => b.textContent?.trim() === 'End')).toBe(false);
  });
});
