/**
 * Pulls the few hard facts that are buried in a product's name.
 *
 * The imported catalogue has no attribute columns — pack size and weight live inside the
 * listing title, the way they do on a marketplace ("... Guggal Combo 90g x 3 Box"). Surfacing
 * them as chips is a reading of text the seller already wrote, not a guess: every pattern here
 * has to match literally, and anything unrecognised simply yields no chip.
 */
export interface ProductFacts {
  /** Net weight or volume, e.g. "90g", "1kg", "500ml". */
  readonly size: string | null;

  /** How many are in the box, e.g. "Pack of 17", "32 pc", "120 Tab". */
  readonly pack: string | null;

  /** Physical size where the listing states one, e.g. "5 inch". */
  readonly dimension: string | null;
}

/** Weight and volume, in the units this catalogue actually uses. */
const SIZE = /\b(\d+(?:\.\d+)?)\s?(kg|g|ml|l)\b/i;

/** "Pack of 17", "Set of 32", "Combo of 2". */
const PACK_OF = /\b(pack|set|combo)\s+of\s+(\d+)\b/i;

/** "(32 pc)", "12 Cups", "120 Tab", "34 Cakes", "6 Piece" — a count and the thing counted. */
const COUNT_OF = /\b(\d+)\s?(pc|pcs|piece|pieces|cups?|cakes?|tabs?|tablets?|n)\b/i;

/** "7 inch", "7.25x7.25 inch". */
const DIMENSION = /\b(\d+(?:\.\d+)?(?:\s?[x×]\s?\d+(?:\.\d+)?)?)\s?(inch|cm|mm)\b/i;

function titleCase(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}

export function productFacts(name: string): ProductFacts {
  const size = SIZE.exec(name);
  const packOf = PACK_OF.exec(name);
  const countOf = packOf ? null : COUNT_OF.exec(name);
  const dimension = DIMENSION.exec(name);

  return {
    // Units are written as the trade writes them: grams and millilitres lower-case, litres and
    // kilograms as "L" and "kg".
    size: size ? `${size[1]}${size[2].toLowerCase() === 'l' ? 'L' : size[2].toLowerCase()}` : null,
    pack: packOf
      ? `${titleCase(packOf[1])} of ${packOf[2]}`
      : countOf
        ? `${countOf[1]} ${titleCase(countOf[2])}`
        : null,
    dimension: dimension ? `${dimension[1].replace(/\s/g, '')} ${dimension[2].toLowerCase()}` : null,
  };
}
