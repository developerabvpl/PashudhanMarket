import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { translations } from '../../i18n/translations';
import { stateLabel } from './state-label';

describe('stateLabel', () => {
  let transloco: TranslocoService;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(translations)] });

    transloco = TestBed.inject(TranslocoService);
    await firstValueFrom(transloco.load('en'));
  });

  it('keeps the English names in English', () => {
    expect(stateLabel(transloco, 'Uttar Pradesh')).toBe('Uttar Pradesh');
    expect(stateLabel(transloco, 'Dadra and Nagar Haveli and Daman and Diu')).toBe('Dadra and Nagar Haveli and Daman and Diu');
  });

  it('names the states in Hindi when the shop is in Hindi', async () => {
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));

    expect(stateLabel(transloco, 'Uttar Pradesh')).toBe('उत्तर प्रदेश');
    expect(stateLabel(transloco, 'Tamil Nadu')).toBe('तमिलनाडु');
    expect(stateLabel(transloco, 'Jammu and Kashmir')).toBe('जम्मू और कश्मीर');
  });

  it('shows a state the API adds later as the API names it', async () => {
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));

    expect(stateLabel(transloco, 'New Territory')).toBe('New Territory');
  });
});
