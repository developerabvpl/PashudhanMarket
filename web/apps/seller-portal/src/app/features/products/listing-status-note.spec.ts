import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { translations } from '../../i18n/translations';
import { listingStatusNoteKey } from './listing-status-note';

describe('listingStatusNoteKey', () => {
  it('has a sentence for each status a saved listing can be in, and none for a new one', () => {
    expect(listingStatusNoteKey('Draft')).toBe('sellerPortal.statusNote.Draft');
    expect(listingStatusNoteKey('InReview')).toBe('sellerPortal.statusNote.InReview');
    expect(listingStatusNoteKey('Active')).toBe('sellerPortal.statusNote.Active');
    expect(listingStatusNoteKey('Archived')).toBe('sellerPortal.statusNote.Archived');
    expect(listingStatusNoteKey(undefined)).toBeNull();
  });

  it('does not call a listing in review, or a draft, live - in English or Hindi', async () => {
    TestBed.configureTestingModule({ providers: [provideI18n(translations)] });
    const transloco = TestBed.inject(TranslocoService);

    for (const lang of ['en', 'hi']) {
      await firstValueFrom(transloco.load(lang));

      const live = transloco.translate('sellerPortal.statusNote.Active', {}, lang);
      const review = transloco.translate('sellerPortal.statusNote.InReview', {}, lang);
      const draft = transloco.translate('sellerPortal.statusNote.Draft', {}, lang);

      expect(new Set([live, review, draft]).size).toBe(3);
      expect(review).toContain(lang === 'en' ? 'moderator' : 'मॉडरेटर');
      expect(review).not.toContain(lang === 'en' ? 'is live' : 'लाइव है');
      expect(draft).not.toContain(lang === 'en' ? 'is live' : 'लाइव है');
    }
  });
});
