/**
 * Indicative prices and opening stock for the imported catalogue.
 *
 * READ THIS BEFORE TRUSTING A NUMBER OUT OF HERE. The source workbook has no price column — it
 * says to check each Amazon listing, because those prices move. Nothing in this file is a real
 * supplier price. What it does is derive a plausible, internally consistent figure from the two
 * facts a listing title does state, the pack size and the weight, against a per-category rate.
 * A 500ml ark therefore costs about twice a 250ml one, and a pack of six soaps costs more than
 * a single bar, which is what makes the storefront usable for design and demo work.
 *
 * Real prices go in tools/data/prices.csv and win over everything here. See the README.
 */

import { createHash } from 'node:crypto';

/**
 * `base` is the rupee price at `reference` grams (or millilitres) for a single unit. The rates
 * are eyeballed from the going rate for each category, not looked up.
 */
const MASS_RATES = {
  AGB: { base: 150, reference: 100 }, // dhoop / agarbatti
  ARK: { base: 210, reference: 500 }, // gomutra ark
  SAB: { base: 65, reference: 100 }, // panchgavya soap bar
  PHN: { base: 200, reference: 1000 }, // floor cleaner
  KHD: { base: 60, reference: 1000 }, // organic manure
  GHN: { base: 260, reference: 25 }, // ghanvati tablets
};

/** Categories sold by the piece rather than by weight. */
const PIECE_RATES = {
  DIY: { base: 9, reference: 12 }, // gobar diya
  SAM: { base: 8, reference: 12 }, // sambrani cup
  KAN: { base: 7, reference: 12 }, // cow dung cake
};

/**
 * Bigger packs cost less per gram. Without this, a 1kg bag priced off a 100g box comes out ten
 * times dearer, which no shelf anywhere works like.
 */
const MASS_TAPER = 0.7;

/** The same idea for counts: a pack of 50 is not fifty times a single. */
const BULK_TAPER = 0.82;

/**
 * Below this, a stated weight is describing one item in a multipack ("Soap, 100g, Pack of 4");
 * at or above it, the weight is the whole package ("1kg Pack, 34 Cakes"). Getting this backwards
 * is what multiplies a kilo of cow dung cakes by the number of cakes in it.
 */
const SMALL_UNIT_GRAMS = 150;

/** Nothing in this catalogue plausibly falls outside these, whatever the arithmetic says. */
const FLOOR = 49;
const CEILING = 2499;

const SIZE = /\b(\d+(?:\.\d+)?)\s?(kg|g|ml|l)\b/i;
const PACK_OF = /\b(?:pack|set|combo)\s+of\s+(\d+)\b/i;
const COUNT_OF = /\b(\d+)\s?(?:pc|pcs|piece|pieces|cups?|cakes?|tabs?|tablets?|n)\b/i;

/** Grams, treating millilitres as grams — near enough for water-based liquids. */
function massInGrams(name) {
  const match = SIZE.exec(name);

  if (!match) {
    return null;
  }

  const value = Number(match[1]);
  const unit = match[2].toLowerCase();

  return unit === 'kg' || unit === 'l' ? value * 1000 : value;
}

/** "Pack of 6" — how many separate items are in the box. */
function packCount(name) {
  const match = PACK_OF.exec(name);

  return match ? Number(match[1]) : null;
}

/** "85 Tablets", "34 Cakes" — how many things are inside, which is not the same thing. */
function contentCount(name) {
  const match = COUNT_OF.exec(name);

  return match ? Number(match[1]) : null;
}

/**
 * Prices that end in 9, the way Indian retail writes them, with the rounding step growing as the
 * number does — 59, 249, 1249 rather than 61, 253, 1247.
 */
function retailRound(value) {
  const clamped = Math.min(CEILING, Math.max(FLOOR, value));

  if (clamped < 100) {
    return Math.round(clamped / 5) * 5 - 1;
  }

  if (clamped < 1000) {
    return Math.round(clamped / 10) * 10 - 1;
  }

  return Math.round(clamped / 50) * 50 - 1;
}

export function indicativePrice(sku, name) {
  const code = sku.split('-')[1];
  const grams = massInGrams(name);
  const packs = packCount(name);
  const contents = contentCount(name);

  const piece = PIECE_RATES[code];

  if (piece) {
    // Either number counts the same thing here: 30 cakes in a "Pack of 30", 34 in a "1kg Pack,
    // 34 Cakes". Whichever the title stated is the quantity being sold.
    return retailRound(piece.base * Math.pow(packs ?? contents ?? piece.reference, BULK_TAPER));
  }

  const rate = MASS_RATES[code];

  if (!rate) {
    throw new Error(`No pricing rule for category code "${code}" (sku ${sku}).`);
  }

  const massFactor = grams === null ? 1 : Math.pow(grams / rate.reference, MASS_TAPER);

  // Only an explicit "Pack of N" multiplies. A bare content count ("85 Tablets") describes what
  // is inside one package, and multiplying by it prices a single strip like a wholesale carton.
  const perUnit = packs !== null && (grams === null || grams < SMALL_UNIT_GRAMS);
  const packFactor = perUnit ? Math.pow(packs, BULK_TAPER) : 1;

  return retailRound(rate.base * massFactor * packFactor);
}

/**
 * Opening stock, deterministic per SKU so a re-import does not reshuffle the shelf.
 *
 * Roughly one listing in twelve lands on zero. That is deliberate: without it the out-of-stock
 * path would never render outside a unit test, and it is the state most likely to be wrong.
 */
export function indicativeStock(sku) {
  const digest = createHash('sha1').update(`stock:${sku}`).digest();
  const roll = digest[0];

  if (roll % 12 === 0) {
    return 0;
  }

  return 6 + (roll % 55);
}
