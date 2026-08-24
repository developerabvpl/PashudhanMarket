import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

/**
 * One hue per category, spread around the wheel so neighbouring tiles stay distinguishable.
 * Keys are the middle segment of the SKU, assigned by tools/scripts/import-catalog.mjs.
 */
const CATEGORY_HUES: Record<string, number> = {
  KAN: 0, // kande — terracotta
  AGB: 40, // agarbatti — ember
  DIY: 80, // diya — lamp gold
  KHD: 120, // khad — leaf
  PHN: 160, // phenyl — pine
  ARK: 200, // gomutra ark — water
  GHN: 240, // ghanvati — indigo
  SAM: 285, // sambrani — incense violet
  SAB: 325, // panchgavya soap — rose
};

/**
 * The photograph for each category, by the middle segment of the SKU.
 *
 * Filenames are listed rather than derived, so the files can keep the names they were supplied
 * with — spaces, mixed case, one .webp among the .jpgs — and so a missing entry is a visible
 * decision rather than a silent 404. Ghanvati has no photograph and is absent on purpose; its
 * cards fall through to the drawn tile.
 */
const CATEGORY_PHOTOS: Record<string, string> = {
  AGB: 'Dhoop.webp',
  ARK: 'gomutra.jpg',
  DIY: 'Cow Dung Diya.jpg',
  KAN: 'Cow dung cake.jpg',
  KHD: 'Khad.jpg',
  PHN: 'Floor cleaner.jpg',
  SAB: 'Soap.jpg',
  SAM: 'haven cup.jpg',
};

/**
 * Product types that have their own picture, checked before the category falls back to its own.
 *
 * A dhoop stick and a bamboo-cored agarbatti are different things on the shelf, and the
 * catalogue has a photograph of each, so the eight listings in that category do not all show
 * the same picture.
 *
 * Order is the whole rule. Half these titles say both words — "Cow Dung Dhoop Agarbatti" — and
 * the product is a dhoop, so dhoop is tested first and the first match wins. Swap the two
 * entries and every listing in the category becomes an agarbatti.
 */
const TYPE_PHOTOS: readonly { readonly code: string; readonly pattern: RegExp; readonly file: string }[] = [
  { code: 'AGB', pattern: /(?:dhoop|dhup|sambrani)/i, file: 'Dhoop.webp' },
  { code: 'AGB', pattern: /(?:agarbatti|incense)/i, file: 'agarbatti.jpg' },
];

/**
 * Where the files live, relative to the served root.
 *
 * Under /media, not /products: a public/products folder shadows the catalogue route of the same
 * name, and the static middleware answers /products with a 301 to /products/ before the router
 * ever sees it.
 */
export const PRODUCT_PHOTO_DIR = '/media/products';

/**
 * The photograph for a product, or null when nothing has been supplied for its category.
 *
 * The filename is percent-encoded because the supplied names contain spaces, and a raw space in
 * an `src` is only forgiven by browsers — it is not forgiven by the crawlers that read the same
 * URL out of the JSON-LD.
 */
export function productPhotoUrl(sku: string, name = ''): string | null {
  const code = sku.split('-')[1] ?? '';

  const byType = TYPE_PHOTOS.find((rule) => rule.code === code && rule.pattern.test(name));
  const file = byType?.file ?? CATEGORY_PHOTOS[code];

  return file ? `${PRODUCT_PHOTO_DIR}/${encodeURIComponent(file)}` : null;
}

/**
 * The product image.
 *
 * Where a category photograph exists that is what shows. Where one does not — or where a listed
 * file has gone missing — this draws a tile instead: a wash keyed to the category with the
 * brand's monogram on top. The catalogue was imported from a listing survey carrying no images,
 * and an `<img>` pointing at a file that is not there renders as a broken icon, which is how the
 * storefront used to look.
 *
 * The image is decorative either way. The product name sits beside it as real text, so a screen
 * reader that announced the picture too would only repeat itself.
 */
@Component({
  selector: 'upb-product-thumb',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block overflow-hidden rounded-card' },
  template: `
    @if (photoUrl(); as url) {
    <img
      class="absolute inset-0 h-full w-full bg-surface object-contain"
      [src]="url"
      alt=""
      aria-hidden="true"
      [attr.loading]="eager() ? 'eager' : 'lazy'"
      [attr.fetchpriority]="eager() ? 'high' : null"
      decoding="async"
      (error)="photoFailed.set(true)"
    />
    } @else {
    <div
      class="absolute inset-0 grid place-items-center"
      [style.background]="wash()"
      aria-hidden="true"
    >
      <!-- A faint concentric motif, so a wall of tiles does not read as flat colour blocks. -->
      <svg class="absolute inset-0 h-full w-full opacity-25" viewBox="0 0 100 100" role="none">
        <circle cx="50" cy="50" r="42" fill="none" stroke="white" stroke-width="0.4" />
        <circle cx="50" cy="50" r="31" fill="none" stroke="white" stroke-width="0.4" />
        <circle cx="50" cy="50" r="20" fill="none" stroke="white" stroke-width="0.4" />
      </svg>

      <span
        class="relative select-none font-semibold tracking-wide text-white"
        [style.font-size]="monogramSize()"
        [style.text-shadow]="'0 1px 2px oklch(0 0 0 / 0.25)'"
      >
        {{ monogram() }}
      </span>
    </div>
    }
  `,
})
export class ProductThumb {
  readonly name = input.required<string>();

  /** Carries the category code as its middle segment, e.g. UPB-AGB-001. */
  readonly sku = input.required<string>();

  readonly size = input<'card' | 'detail'>('card');

  /**
   * Load this one immediately rather than lazily.
   *
   * Set it on whatever is above the fold. The first card in the catalogue is the page's Largest
   * Contentful Paint element, and lazy-loading the LCP image delays the metric by a whole
   * request — Angular warns about exactly this (NG0913).
   */
  readonly priority = input(false);

  /** The product page's image is always the LCP candidate; in the grid, only the first row is. */
  protected readonly eager = computed(() => this.priority() || this.size() === 'detail');

  /** Set once the browser reports the file did not load, which drops us to the drawn tile. */
  protected readonly photoFailed = signal(false);

  protected readonly photoUrl = computed(() =>
    this.photoFailed() ? null : productPhotoUrl(this.sku(), this.name())
  );

  /**
   * Hue comes from the SKU's category segment, so every product in a category shares a tint and
   * the grid reads as grouped even before the labels are scanned.
   *
   * The known codes get hand-picked hues rather than a hash, because hashing put agarbatti and
   * gomutra ark within a few degrees of each other and the grid lost the grouping it was
   * supposed to gain. Anything unrecognised falls back to the hash.
   */
  private readonly hue = computed(() => {
    const code = this.sku().split('-')[1] ?? this.sku();
    const known = CATEGORY_HUES[code];

    if (known !== undefined) {
      return known;
    }

    let hash = 0;

    for (const character of code) {
      hash = (hash * 31 + character.charCodeAt(0)) % 360;
    }

    return hash;
  });

  protected readonly wash = computed(() => {
    const hue = this.hue();

    return (
      `linear-gradient(140deg, oklch(0.62 0.13 ${hue}) 0%, ` +
      `oklch(0.48 0.11 ${(hue + 28) % 360}) 100%)`
    );
  });

  protected readonly monogram = computed(() => {
    const words = this.name()
      .replace(/[^\p{L}\p{N} ]/gu, ' ')
      .split(/\s+/)
      .filter(Boolean);

    return words
      .slice(0, 2)
      .map((word) => word[0].toUpperCase())
      .join('');
  });

  protected readonly monogramSize = computed(() => (this.size() === 'detail' ? '4rem' : '1.75rem'));
}
