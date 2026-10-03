import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import {
  Api,
  SellerOrderDto,
  apiV1SellerOrdersOrderIdGet,
  apiV1SellerShippingOrdersOrderIdShipmentsGet,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { translations } from '../../i18n/translations';
import { OrderPage } from './order.page';

/** Delivered: two products at ₹557 full price, ₹50 of it taken off by a coupon. */
const order = {
  orderId: 'o1',
  orderNumber: 'UPB-261003-AAAAAA',
  partId: 'part1',
  status: 'Delivered',
  paymentMethod: 'Online',
  codAmount: 0,
  subtotal: 557,
  discount: 50,
  discountFundedBy: 'Seller',
  currency: 'INR',
  placedAtUtc: '2026-10-03T06:00:00Z',
  deliveryAddress: { fullName: 'Asha Devi', mobile: '9876543210', line1: '12 Gaushala Road', line2: null, landmark: null, city: 'Lucknow', district: null, state: 'Uttar Pradesh', pincode: '226001' },
  lines: [
    { productId: 'p1', sku: 'GHEE-1', name: 'Desi ghee', unitPrice: 239, quantity: 2, lineTotal: 478, discount: 43, returnQuantity: 0 },
    { productId: 'p2', sku: 'DIYA-12', name: 'Gobar Diya', unitPrice: 79, quantity: 1, lineTotal: 79, discount: 7, returnQuantity: 0 },
  ],
  cancellationReason: null,
  returnCondition: null,
  returnRequest: null,
} as unknown as SellerOrderDto;

/** The same part after the buyer sent one ghee back and it was found damaged. */
const returned = {
  ...order,
  status: 'Returned',
  returnCondition: 'Damaged',
  lines: [{ ...order.lines[0], returnQuantity: 1 }, order.lines[1]],
  returnRequest: { status: 'Approved', reason: 'Damaged', comment: null, refundUpiId: null, requestedAtUtc: '2026-10-05T06:00:00Z', decisionNote: null, decidedAtUtc: null, refundDue: 217.5 },
} as unknown as SellerOrderDto;

async function render(found: SellerOrderDto, lang: 'en' | 'hi' = 'en') {
  const invoke = vi.fn(async (fn: unknown) => {
    if (fn === apiV1SellerOrdersOrderIdGet) {
      return found;
    }

    if (fn === apiV1SellerShippingOrdersOrderIdShipmentsGet) {
      return [];
    }

    throw new Error('unexpected call');
  });

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(translations), provideRouter([]), { provide: Api, useValue: { invoke } }],
  });

  const transloco = TestBed.inject(TranslocoService);
  transloco.setActiveLang(lang);
  await firstValueFrom(transloco.load(lang));

  const fixture = TestBed.createComponent(OrderPage);
  fixture.componentRef.setInput('orderId', 'o1');
  await fixture.whenStable();
  // The page loads from an effect, which nothing waits for: let its two requests settle, then draw.
  await new Promise((resolve) => setTimeout(resolve));
  await fixture.whenStable();

  const element = fixture.nativeElement as HTMLElement;

  return { element, text: element.textContent?.replace(/\s+/g, ' ') ?? '' };
}

describe('seller OrderPage', () => {
  it('shows the coupon discount on the part and what the buyer pays for the goods', async () => {
    const { text } = await render(order);

    expect(text).toContain('Value ₹557.00');
    expect(text).toContain('Coupon discount −₹50.00');
    expect(text).toContain('Buyer pays ₹507.00 for the goods');
  });

  it('tells the seller when the discount comes out of their own earnings', async () => {
    const { text } = await render(order);

    expect(text).toContain('You fund this coupon');
  });

  it('tells the seller when the platform bears the discount instead', async () => {
    const { text } = await render({ ...order, discountFundedBy: 'Platform' });

    expect(text).toContain('UP Bazaar funds this coupon');
    expect(text).not.toContain('You fund this coupon');
  });

  it('says nothing of a discount on a part no coupon touched', async () => {
    const { text } = await render({ ...order, discount: 0, discountFundedBy: null, lines: order.lines.map((l) => ({ ...l, discount: 0 })) });

    expect(text).toContain('Value ₹557.00');
    expect(text).not.toContain('Coupon discount');
    expect(text).not.toContain('funds this coupon');
  });

  it('titles the card that says what the inspection of a returned parcel found', async () => {
    const { element } = await render(returned);

    const card = [...element.querySelectorAll('.upb-card')].find((c) => c.textContent?.includes('Returned damaged; not restocked.'));
    expect(card?.querySelector('h2')?.textContent?.trim()).toBe('Return inspection');
  });

  it('counts one unit that came back in the singular, in Hindi too', async () => {
    expect((await render(returned)).text).toContain('1 came back');

    TestBed.resetTestingModule();
    const hindi = await render(returned, 'hi');

    expect(hindi.text).toContain('1 वापस आ गया');
    expect(hindi.text).not.toContain('1 वापस आ गए');
    expect(hindi.text).toContain('वापसी की जाँच');
  });
});
