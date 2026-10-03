/**
 * A portal toolbar's navigation, as data: each portal lists its pages once, and the shell draws
 * them as buttons and drop-downs on a wide screen and as one menu on a narrow one. Drawing both
 * from one list keeps the two from disagreeing about what is there.
 */

/** One page in the toolbar. */
export interface PortalNavLink {
  readonly kind: 'link';
  /** i18n key. */
  readonly label: string;
  readonly route: string;
  /** The permission the page's route guard asks for; none for a page every signed-in user may open. */
  readonly permission?: string;
  /** Active only on exactly this route, for a page whose children have entries of their own. */
  readonly exact?: boolean;
}

/** Pages that share a drop-down on a wide toolbar, and a heading in the narrow menu. */
export interface PortalNavGroup {
  readonly kind: 'group';
  /** i18n key. */
  readonly label: string;
  readonly links: readonly PortalNavLink[];
}

export type PortalNavEntry = PortalNavLink | PortalNavGroup;

/**
 * What this user may open. A link whose permission they lack is left out, so the toolbar never
 * offers a page that would answer 403 - the route guard still enforces it, this keeps the UI
 * honest. A group left with nothing is dropped, and one left with a single page becomes that page,
 * since a drop-down holding one item is a click for nothing.
 */
export function visibleNav(
  entries: readonly PortalNavEntry[],
  has: (permission: string) => boolean
): PortalNavEntry[] {
  const allowed = (link: PortalNavLink) => link.permission === undefined || has(link.permission);

  return entries.flatMap((entry): PortalNavEntry[] => {
    if (entry.kind === 'link') {
      return allowed(entry) ? [entry] : [];
    }

    const links = entry.links.filter(allowed);

    if (links.length === 0) {
      return [];
    }

    return links.length === 1 ? [links[0]] : [{ ...entry, links }];
  });
}

/**
 * Whether an entry of the narrow menu needs a rule drawn above it: a page of its own that comes
 * straight after a group. A group opens with its name, which sets it apart from whatever is above;
 * a lone page has no heading, so without the rule it reads as one more page of the group before
 * it - "Sellers" under "Shipping" in the admin menu. Pages that follow one another need nothing.
 */
export function startsOwnSection(entries: readonly PortalNavEntry[], index: number): boolean {
  return index > 0 && entries[index].kind === 'link' && entries[index - 1].kind === 'group';
}
