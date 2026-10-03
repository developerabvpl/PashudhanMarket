import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { LANGUAGE_COOKIE, LANGUAGE_PENDING_CLASS } from '@upbazaar/ui';

/**
 * The few lines in index.html that run before the app does. They are not part of any bundle, so
 * nothing else compiles or exercises them: this runs the script as the page carries it.
 */
const html = readFileSync(resolve(__dirname, 'index.html'), 'utf8');
const script = /<script>([\s\S]*?)<\/script>/.exec(html)?.[1] ?? '';

interface FakeTimer {
  run: () => void;
  delay: number;
}

function boot(cookie: string, servedLang = 'en'): { page: Document; timers: FakeTimer[] } {
  const page = document.implementation.createHTMLDocument('served');
  const timers: FakeTimer[] = [];

  page.documentElement.setAttribute('lang', servedLang);
  Object.defineProperty(page, 'cookie', { value: cookie });

  new Function('document', 'setTimeout', script)(page, (run: () => void, delay: number) => timers.push({ run, delay }));

  return { page, timers };
}

describe('index.html language boot', () => {
  it('is there, and uses the names the app uses', () => {
    expect(script).toContain(LANGUAGE_PENDING_CLASS);
    // The script names the cookie inside a regular expression, its dot escaped.
    expect(script.split(String.fromCharCode(92)).join('')).toContain(LANGUAGE_COOKIE);
    expect(html).toContain(`html.${LANGUAGE_PENDING_CLASS} upb-root`);
  });

  it('marks an English page Hindi for a Hindi visitor, and holds it back until the app redraws it', () => {
    const { page, timers } = boot('theme=dark; upb.lang=hi');

    expect(page.documentElement.lang).toBe('hi');
    expect(page.documentElement.classList.contains(LANGUAGE_PENDING_CLASS)).toBe(true);

    // If the app never starts, the English page shows after all rather than staying blank.
    expect(timers).toHaveLength(1);
    timers[0].run();
    expect(page.documentElement.classList.contains(LANGUAGE_PENDING_CLASS)).toBe(false);
    expect(page.documentElement.lang).toBe('hi');
  });

  it('leaves the page alone for an English visitor, one with no choice, and a value it does not know', () => {
    for (const cookie of ['upb.lang=en', '', 'upb.lang=fr', 'xupb.lang=hi']) {
      const { page, timers } = boot(cookie);

      expect(page.documentElement.lang).toBe('en');
      expect(page.documentElement.classList.contains(LANGUAGE_PENDING_CLASS)).toBe(false);
      expect(timers).toHaveLength(0);
    }
  });

  it('holds nothing back when the server already rendered the page in the visitor’s language', () => {
    const { page, timers } = boot('upb.lang=hi', 'hi');

    expect(page.documentElement.classList.contains(LANGUAGE_PENDING_CLASS)).toBe(false);
    expect(timers).toHaveLength(0);
  });
});
