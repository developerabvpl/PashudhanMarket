import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Api, SellerDto, apiV1SellersMeApplicationPut } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { SellerAccess } from '../../core/seller-access';
import { translations } from '../../i18n/translations';
import { ApplyPage } from './apply.page';

/** A shop as the API returns it to its owner: the account number is never there, only its last four digits. */
const seller: SellerDto = {
  id: 's1',
  shopName: 'Gaushala Lucknow',
  description: null,
  status: 'Pending',
  contactMobile: '9876543210',
  contactEmail: null,
  address: { line1: '12 Gaushala Road', line2: null, city: 'Lucknow', state: 'Uttar Pradesh', pincode: '226001' },
  kyc: { legalName: 'Kavita Singh', pan: 'AAAAA0000A', gstin: null, bankAccountHolder: 'Kavita Singh', bankAccountLast4: '5566', ifsc: 'SBIN0001234' },
  ownerUserId: 'u1',
  reviewNote: null,
  reviewedAtUtc: null,
  submittedAtUtc: '2026-10-01T10:00:00Z',
};

async function render(found: SellerDto) {
  const current = signal<SellerDto | null>(found);
  const invoke = vi.fn(async () => found);

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(translations),
      provideRouter([]),
      { provide: Api, useValue: { invoke } },
      { provide: SellerAccess, useValue: { seller: current, load: async () => found, set: (s: SellerDto) => current.set(s), refreshAccess: async () => true } },
    ],
  });

  const fixture = TestBed.createComponent(ApplyPage);
  await fixture.whenStable();
  // ngOnInit fills the form after its load resolves; let that settle, then draw.
  await new Promise((resolve) => setTimeout(resolve));
  await fixture.whenStable();

  const element = fixture.nativeElement as HTMLElement;
  const account = element.querySelector('input[name="bankAccountNumber"]') as HTMLInputElement;

  return { fixture, element, account, invoke };
}

describe('ApplyPage', () => {
  it('shows a pending application its bank account number masked to the last four digits, not empty', async () => {
    const { element, account } = await render(seller);

    expect(account.readOnly).toBe(true);
    expect(account.value).toBe('•••• 5566');
    expect(element.querySelector('[data-bank-note]')?.textContent?.trim()).toBe('Only the last four digits are shown, for your security.');
    // The rest of what was submitted is there in full.
    expect((element.querySelector('input[name="ifsc"]') as HTMLInputElement).value).toBe('SBIN0001234');
  });

  it('asks a rejected applicant to type the number again, and never submits the mask', async () => {
    const { element, account, invoke, fixture } = await render({ ...seller, status: 'Rejected', reviewNote: 'PAN does not match.' });

    expect(account.readOnly).toBe(false);
    expect(account.value).toBe('');
    expect(element.querySelector('[data-bank-note]')?.textContent).toContain('ending 5566');

    account.value = '1234 5678 9012';
    account.dispatchEvent(new Event('input'));
    element.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellersMeApplicationPut, { body: expect.objectContaining({ bankAccountNumber: '123456789012' }) });
  });

  it('says nothing about a hidden number to someone who has not applied yet', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n(translations),
        provideRouter([]),
        { provide: Api, useValue: { invoke: vi.fn() } },
        { provide: SellerAccess, useValue: { seller: signal(null), load: async () => null } },
      ],
    });

    const fixture = TestBed.createComponent(ApplyPage);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect((element.querySelector('input[name="bankAccountNumber"]') as HTMLInputElement).value).toBe('');
    expect(element.querySelector('[data-bank-note]')).toBeNull();
  });
});
