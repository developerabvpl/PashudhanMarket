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
 * Categories with a photograph in apps/storefront/public/media/products.
 *
 * Listed rather than probed per product: asking for a per-SKU file on every card would 404
 * seventy times over, since the photography is per category for now.
 *
 * A code here is a promise that the file exists. Ghanvati is absent because no photograph was
 * supplied for it, and it should stay absent until one is. If a listed file is missing the card
 * still renders — the (error) handler drops it to the drawn tile — but the browser pays for the
 * failed request first, so keep this set and the folder in step.
 */
const CATEGORY_PHOTOS = new Set(['AGB', 'ARK', 'DIY', 'KAN', 'KHD', 'PHN', 'SAB', 'SAM']);

/**
 * Where the files live, relative to the served root.
 *
 * Under /media, not /products: a public/products folder shadows the catalogue route of the same
 * name, and the static middleware answers /products with a 301 to /products/ before the router
 * ever sees it.
 */
export const PRODUCT_PHOTO_DIR = '/media/products';

/** The photograph for a SKU's category, or null when that category has none yet. */
export function categoryPhotoUrl(sku: string): string | null {
  const code = sku.split('-')[1] ?? '';

  return CATEGORY_PHOTOS.has(code) ? `${PRODUCT_PHOTO_DIR}/${code.toLowerCase()}.jpg` : null;
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
  host: { class: 'block' },
  template: `
    @if (photoUrl(); as url) {
    <img
      class="h-full w-full rounded-card bg-surface-sunken object-cover"
      [src]="url"
      alt=""
      aria-hidden="true"
      [attr.loading]="size() === 'detail' ? 'eager' : 'lazy'"
      [attr.fetchpriority]="size() === 'detail' ? 'high' : null"
      decoding="async"
      (error)="photoFailed.set(true)"
    />
    } @else {
    <div
      class="relative grid h-full w-full place-items-center overflow-hidden rounded-card"
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

  /** Set once the browser reports the file did not load, which drops us to the drawn tile. */
  protected readonly photoFailed = signal(false);

  protected readonly photoUrl = computed(() =>
    this.photoFailed() ? null : categoryPhotoUrl(this.sku())
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
