import { firstValueFrom, isObservable } from 'rxjs';
import { Translation } from '@jsverse/transloco';
import { BundledTranslocoLoader } from './provide-i18n';

async function load(lang: string): Promise<Translation> {
  const result = new BundledTranslocoLoader().getTranslation(lang);

  return isObservable(result) ? firstValueFrom(result) : result;
}

describe('BundledTranslocoLoader', () => {
  it('has English to hand, and loads Hindi when it is asked for', async () => {
    const en = await load('en');
    const hi = await load('hi');

    expect(en['nav']['team']).toBe('Team');
    expect(hi['nav']['team']).toBe('टीम');
  });

  it('falls back to English for a language it does not have', async () => {
    expect((await load('ta'))['nav']['team']).toBe('Team');
  });
});
