import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CurrentUserStore } from '@upbazaar/auth';
import { Api, SellerDto, apiV1AdminSellersGet, apiV1AdminSellersSellerIdGet } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { SellersPage } from './sellers.page';

const seller = {
  id: 's1',
  shopName: 'Yadav Gaushala',
  description: null,
  contactMobile: '9000000000',
  contactEmail: null,
  status: 'Approved',
  submittedAtUtc: '2026-09-01T10:00:00Z',
  reviewNote: null,
  reviewedAtUtc: null,
  address: { line1: '5 Dairy Lane', line2: null, city: 'Lucknow', state: 'Uttar Pradesh', pincode: '226001' },
  kyc: { legalName: 'Ramesh Yadav', pan: 'AAAAA0000A', gstin: null, bankAccountHolder: 'Ramesh Yadav', bankAccountLast4: '5566', ifsc: 'SBIN0001234' },
  ownerUserId: 'u1',
  ownerName: 'Ramesh Yadav',
  ownerEmail: 'owner@upbazaar.test',
} as unknown as SellerDto;

/** Opens the one application in the list and returns the owner line as a screen reads it. */
async function ownerLine(detail: SellerDto): Promise<string> {
  const invoke = vi.fn(async (fn: unknown) => {
    if (fn === apiV1AdminSellersGet) {
      return { items: [detail], totalCount: 1 };
    }

    if (fn === apiV1AdminSellersSellerIdGet) {
      return detail;
    }

    throw new Error('unexpected call');
  });

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(translations),
      { provide: Api, useValue: { invoke } },
      { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
    ],
  });

  const fixture = TestBed.createComponent(SellersPage);
  await fixture.whenStable();

  (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('li button')!.click();
  await fixture.whenStable();

  const terms = [...(fixture.nativeElement as HTMLElement).querySelectorAll('dt')];
  const owner = terms.find((dt) => dt.textContent?.trim() === 'Owner account')!;

  return owner.nextElementSibling?.textContent?.trim() ?? '';
}

describe('SellersPage owner line', () => {
  it('puts a space on both sides of the dot between the owner and their email', async () => {
    expect(await ownerLine(seller)).toBe('Ramesh Yadav · owner@upbazaar.test');
  });

  it('shows the name alone when the account has no email', async () => {
    expect(await ownerLine({ ...seller, ownerEmail: null })).toBe('Ramesh Yadav');
  });
});
