import { PortalNavEntry } from '@upbazaar/auth';
import en from '../i18n/en.json';
import hi from '../i18n/hi.json';
import { ADMIN_NAV } from './nav';

function link(route: string): PortalNavEntry | undefined {
  return ADMIN_NAV.flatMap((entry) => (entry.kind === 'group' ? entry.links : [entry])).find((entry) => entry.kind === 'link' && entry.route === route);
}

describe('ADMIN_NAV', () => {
  it('names the Shipping link for the page it opens: pickup locations, not a list of shipments', () => {
    // /shipping redirects to the pickup locations page (shipping.routes.ts); there is no shipments list.
    const entry = link('/shipping');

    expect(entry?.label).toBe('nav.pickupLocations');
    expect(en.nav.pickupLocations).toBe(en.shipping.pickupTitle);
    expect(hi.nav.pickupLocations).toBe(hi.shipping.pickupTitle);
  });

  it('has a label in both languages for every entry', () => {
    const labels = ADMIN_NAV.flatMap((entry) => (entry.kind === 'group' ? [entry, ...entry.links] : [entry])).map((entry) => entry.label.replace('nav.', ''));

    for (const label of labels) {
      expect(Object.keys(en.nav)).toContain(label);
      expect(Object.keys(hi.nav)).toContain(label);
    }
  });
});
