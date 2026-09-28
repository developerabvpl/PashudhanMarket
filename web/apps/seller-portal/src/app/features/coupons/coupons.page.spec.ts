import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import {
  Api,
  CouponDto,
  apiV1SellerPromotionsCampaignsCouponIdJoinedPut,
  apiV1SellerPromotionsCampaignsGet,
  apiV1SellerPromotionsCouponsGet,
  apiV1SellerPromotionsCouponsPost,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { CouponsPage } from './coupons.page';

function coupon(overrides: Partial<CouponDto>): CouponDto {
  return {
    id: 'c1',
    code: 'SHOP20',
    description: 'Shop sale',
    sellerId: 's1',
    fundedBy: 'Seller',
    discountType: 'Flat',
    value: 20,
    maxDiscount: null,
    minOrderValue: null,
    startsAtUtc: '2026-09-25T00:00:00Z',
    endsAtUtc: null,
    totalLimit: null,
    perBuyerLimit: 1,
    uses: 0,
    isActive: true,
    sellersJoined: 0,
    joined: false,
    ...overrides,
  };
}

async function render() {
  const campaign = coupon({ id: 'k1', code: 'DIWALI', sellerId: null, discountType: 'Percent', value: 10 });
  const invoke = vi.fn(async (fn: unknown) => {
    switch (fn) {
      case apiV1SellerPromotionsCouponsGet:
        return [coupon({})];
      case apiV1SellerPromotionsCampaignsGet:
        return [campaign];
      default:
        return campaign;
    }
  });

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(translations), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(CouponsPage);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

describe('CouponsPage', () => {
  it('lists the seller’s coupons and the campaigns they can join', async () => {
    const { element } = await render();

    expect(element.textContent).toContain('SHOP20');
    expect(element.textContent).toMatch(/₹20(\.00)? off/);
    expect(element.textContent).toContain('DIWALI');
    expect(element.textContent).toContain('You pay for the discount');
  });

  it('creates a coupon without asking who pays', async () => {
    const { fixture, invoke, element } = await render();

    for (const [name, value] of [['code', 'SHOP30'], ['description', 'Big sale'], ['value', '30']]) {
      const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerPromotionsCouponsPost, {
      body: { code: 'SHOP30', description: 'Big sale', discountType: 'Percent', value: 30, minOrderValue: null, endsAtUtc: null, totalLimit: null },
    });
  });

  it('joins a campaign', async () => {
    const { fixture, invoke, element } = await render();

    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Join')!.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerPromotionsCampaignsCouponIdJoinedPut, { couponId: 'k1', body: { joined: true } });
  });
});
