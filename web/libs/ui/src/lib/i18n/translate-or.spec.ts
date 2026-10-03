import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { provideI18n } from './provide-i18n';
import { translateOr } from './translate-or';

describe('translateOr', () => {
  let transloco: TranslocoService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n({
          en: () => Promise.resolve({ staff: { roleNames: { Buyer: 'Buyer', Admin: 'Administrator' } } }),
          hi: () => Promise.resolve({ staff: { roleNames: { Buyer: 'खरीदार' } } }),
        }),
      ],
    });

    transloco = TestBed.inject(TranslocoService);
    await firstValueFrom(transloco.load('en'));
  });

  it('translates a name it has words for', () => {
    expect(translateOr(transloco, 'staff.roleNames.Buyer', 'Buyer')).toBe('Buyer');
  });

  it('follows the language showing', async () => {
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));

    expect(translateOr(transloco, 'staff.roleNames.Buyer', 'Buyer')).toBe('खरीदार');
  });

  it('reads in English when only English has the words', async () => {
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));

    expect(translateOr(transloco, 'staff.roleNames.Admin', 'Admin')).toBe('Administrator');
  });

  it('shows the name as the server sent it, not a raw key, when it is one it does not know', () => {
    expect(translateOr(transloco, 'staff.roleNames.Auditor', 'Auditor')).toBe('Auditor');
  });
});
