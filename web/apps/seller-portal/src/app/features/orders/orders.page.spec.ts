import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { providePortalPaginatorIntl } from '@upbazaar/auth';
import { Api, SellerOrderSummaryDto } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { translations } from '../../i18n/translations';
import { OrdersPage } from './orders.page';

function summary(overrides: Partial<SellerOrderSummaryDto>): SellerOrderSummaryDto {
  return {
    city: 'Lucknow',
    codAmount: 0,
    currency: 'INR',
    itemCount: 1,
    orderId: 'o1',
    orderNumber: 'UPB-260929-AAAAAA',
    partId: 'part1',
    paymentMethod: 'Online',
    placedAtUtc: '2026-09-29T10:00:00Z',
    returnRequestStatus: null,
    status: 'Confirmed',
    subtotal: 500,
    ...overrides,
  };
}

async function render(lang: 'en' | 'hi') {
  const items = [summary({}), summary({ orderId: 'o2', partId: 'part2', orderNumber: 'UPB-260929-BBBBBB', itemCount: 3 })];
  const invoke = vi.fn(async () => ({ items, page: 1, pageSize: 25, totalCount: 2, hasNextPage: false, totalPages: 1 }));

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideRouter([]),
      provideI18n(translations),
      providePortalPaginatorIntl(),
      { provide: Api, useValue: { invoke } },
    ],
  });

  const transloco = TestBed.inject(TranslocoService);
  transloco.setActiveLang(lang);
  await firstValueFrom(transloco.load(lang));

  const fixture = TestBed.createComponent(OrdersPage);

  /** The page's text once it has settled, whitespace collapsed. */
  return async () => {
    await fixture.whenStable();

    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
  };
}

describe('OrdersPage', () => {
  it('counts one item and several items in English', async () => {
    const text = await (await render('en'))();

    expect(text).toContain('1 item ');
    expect(text).not.toContain('1 items');
    expect(text).toContain('3 items');
  });

  it('counts items and labels the paginator in Hindi', async () => {
    const textOf = await render('hi');

    // The paginator's labels arrive in their own chunk, a moment after the page.
    await vi.waitFor(async () => expect(await textOf()).toContain(`2 में से 1 ${String.fromCharCode(0x2013)} 2`));

    const text = await textOf();
    expect(text).toContain('1 वस्तु ');
    expect(text).toContain('3 वस्तुएँ');
    expect(text).not.toContain(' of 2');
  });
});
