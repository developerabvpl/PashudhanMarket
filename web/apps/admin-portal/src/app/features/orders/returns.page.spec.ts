import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { CurrentUserStore } from '@upbazaar/auth';
import { Api, ReturnRequestSummaryDto, apiV1AdminOrdersReturnsGet } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { ReturnsPage } from './returns.page';

const request: ReturnRequestSummaryDto = {
  orderId: 'o1',
  orderNumber: 'UPB-260923-ABCDEF',
  partId: 'part1',
  sellerId: 's1',
  partStatus: 'Delivered',
  status: 'Requested',
  reason: 'WrongItem',
  comment: 'Got dhoop instead of diyas.',
  paymentMethod: 'CashOnDelivery',
  subtotal: 150,
  currency: 'INR',
  requestedAtUtc: '2026-09-23T10:00:00Z',
};

describe('ReturnsPage', () => {
  it('opens on requests still waiting for a decision and links each to its order', async () => {
    const invoke = vi.fn(async () => ({ items: [request], page: 1, pageSize: 25, totalCount: 1, hasNextPage: false, totalPages: 1 }));

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideI18n(translations),
        { provide: Api, useValue: { invoke } },
        { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
      ],
    });

    const fixture = TestBed.createComponent(ReturnsPage);
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1AdminOrdersReturnsGet, { Page: 1, PageSize: 25, Status: 'Requested' });

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('I received the wrong item');
    expect(text).toContain('Got dhoop instead of diyas.');

    const link = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/orders?orderId=o1');
  });

  it('reads an accepted return that is back with the seller as returned, not as an undelivered parcel', async () => {
    const back = { ...request, status: 'Approved', partStatus: 'Returned', partReturn: 'Full' };
    const partly = { ...request, partId: 'part2', status: 'Approved', partStatus: 'Returned', partReturn: 'Partial' };
    const invoke = vi.fn(async () => ({ items: [back, partly], page: 1, pageSize: 25, totalCount: 2, hasNextPage: false, totalPages: 1 }));

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideI18n(translations),
        { provide: Api, useValue: { invoke } },
        { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
      ],
    });

    const fixture = TestBed.createComponent(ReturnsPage);
    await fixture.whenStable();

    const rows = [...fixture.nativeElement.querySelectorAll('tr[mat-row]')].map((row) => (row as HTMLElement).textContent ?? '');

    expect(rows[0]).toContain('Returned');
    expect(rows[1]).toContain('Partly returned');
    expect(fixture.nativeElement.textContent).not.toContain('Could not be delivered');
  });
});
