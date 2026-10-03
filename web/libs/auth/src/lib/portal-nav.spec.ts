import { PortalNavEntry, startsOwnSection, visibleNav } from './portal-nav';

const NAV: readonly PortalNavEntry[] = [
  { kind: 'link', label: 'nav.staffUsers', route: '/staff', permission: 'users.read' },
  {
    kind: 'group',
    label: 'nav.orders',
    links: [
      { kind: 'link', label: 'nav.orders', route: '/orders', permission: 'orders.read', exact: true },
      { kind: 'link', label: 'nav.returns', route: '/orders/returns', permission: 'orders.read' },
    ],
  },
  {
    kind: 'group',
    label: 'nav.money',
    links: [
      { kind: 'link', label: 'nav.payments', route: '/payments', permission: 'payments.read' },
      { kind: 'link', label: 'nav.payouts', route: '/settlements/payouts', permission: 'settlements.read' },
    ],
  },
  { kind: 'link', label: 'nav.team', route: '/team' },
];

const labels = (entries: readonly PortalNavEntry[]) =>
  entries.map((e) => (e.kind === 'group' ? `${e.label}[${e.links.map((l) => l.label).join(',')}]` : e.label));

describe('visibleNav', () => {
  it('keeps everything for someone with every permission', () => {
    expect(labels(visibleNav(NAV, () => true))).toEqual([
      'nav.staffUsers',
      'nav.orders[nav.orders,nav.returns]',
      'nav.money[nav.payments,nav.payouts]',
      'nav.team',
    ]);
  });

  it('drops pages the user may not open, and groups left empty', () => {
    expect(labels(visibleNav(NAV, (p) => p === 'orders.read'))).toEqual(['nav.orders[nav.orders,nav.returns]', 'nav.team']);
  });

  it('turns a group left with one page into that page', () => {
    const entries = visibleNav(NAV, (p) => p === 'payments.read');

    expect(entries).toContainEqual({ kind: 'link', label: 'nav.payments', route: '/payments', permission: 'payments.read' });
    expect(labels(entries)).toEqual(['nav.payments', 'nav.team']);
  });
});

describe('startsOwnSection', () => {
  const marked = (entries: readonly PortalNavEntry[]) =>
    entries.filter((_, i) => startsOwnSection(entries, i)).map((e) => e.label);

  it('rules off a page of its own that follows a group, so it does not read as part of it', () => {
    // nav.team comes straight after the Money group, as Sellers follows Shipping in the admin menu.
    expect(marked(NAV)).toEqual(['nav.team']);
  });

  it('leaves groups, which carry their own heading, and the first entry alone', () => {
    expect(startsOwnSection(NAV, 0)).toBe(false);
    expect(startsOwnSection(NAV, 1)).toBe(false);
    expect(startsOwnSection(NAV, 2)).toBe(false);
  });

  it('draws nothing between pages that follow one another', () => {
    // The seller menu: its own pages first, then one group - and that group shrunk to one page.
    const pages = visibleNav(NAV, (p) => p === 'payments.read');

    expect(marked(pages)).toEqual([]);
  });
});
