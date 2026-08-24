import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

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
 * The tile that stands in for a product photograph.
 *
 * The catalogue has no images — the source workbook is a listing survey, not a photo shoot —
 * and an `<img>` pointing at a file that does not exist renders as a broken-image glyph, which
 * is how the storefront looked before. This draws something deliberate instead: a wash keyed to
 * the product's category and the brand's monogram on top.
 *
 * It is decorative, so it is hidden from assistive technology; the product name sits beside it
 * as real text. Replace the whole component with an `<img>` the day catalogue images land.
 */
@Component({
  selector: 'upb-product-thumb',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
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
  `,
})
export class ProductThumb {
  readonly name = input.required<string>();

  /** Carries the category code as its middle segment, e.g. UPB-AGB-001. */
  readonly sku = input.required<string>();

  readonly size = input<'card' | 'detail'>('card');

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
