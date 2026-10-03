import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import { UserSummaryDto } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { translations } from '../../i18n/translations';
import { StaffListPage } from './staff-list.page';
import { StaffService } from './staff.service';

const seller: UserSummaryDto = {
  id: 'u1',
  displayName: 'Gaushala Owner',
  email: 'owner@example.com',
  mobile: null,
  roles: [],
  status: 'Suspended',
  userType: 'Seller',
  createdAtUtc: '2026-09-01T10:00:00Z',
};

/** Holds a role the portal has a label for, and one created on the server that it has not. */
const dispatcher: UserSummaryDto = { ...seller, id: 'u2', displayName: 'Packing Desk', roles: ['SellerDispatch', 'Auditor'] };

async function render() {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(translations),
      { provide: StaffService, useValue: { list: vi.fn(async () => ({ items: [seller, dispatcher], totalCount: 2 })) } },
      { provide: CurrentUserStore, useValue: { has: () => true, hasAll: () => true } },
    ],
  });

  const fixture = TestBed.createComponent(StaffListPage);
  await fixture.whenStable();

  return { fixture, element: fixture.nativeElement as HTMLElement };
}

function cells(element: HTMLElement): string[] {
  return [...element.querySelectorAll('td')].map((td) => td.textContent?.trim() ?? '');
}

describe('StaffListPage', () => {
  it('names the account type and status in words, not the API values', async () => {
    const { element } = await render();

    expect(cells(element)).toContain('Seller');
    expect(cells(element)).toContain('Suspended');
  });

  it('names them in Hindi too', async () => {
    const { fixture, element } = await render();

    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));
    await fixture.whenStable();

    expect(cells(element)).toContain('विक्रेता');
    expect(cells(element)).toContain('निलंबित');
  });

  it('names roles in words, and leaves one it has no label for as the server named it', async () => {
    const { element } = await render();

    expect(cells(element)).toContain('Seller dispatch, Auditor');
    expect(element.textContent).not.toContain('staff.roleNames');
  });

  it('names roles in Hindi too', async () => {
    const { fixture, element } = await render();

    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));
    await fixture.whenStable();

    expect(cells(element)).toContain('विक्रेता डिस्पैच, Auditor');
  });
});
