#!/usr/bin/env node
/**
 * Turns the Gaushala product workbook into the catalogue the stub API serves.
 *
 *   node tools/scripts/import-catalog.mjs
 *   node tools/scripts/import-catalog.mjs --source path/to/other.xlsx
 *
 * The workbook is a market survey of Amazon.in listings: category, product name, brand. It
 * carries no price, no stock and no images.
 *
 * Prices come from two places, in this order:
 *
 *   1. tools/data/prices.csv — "sku,price,stock". Real figures, once sellers supply them.
 *   2. tools/scripts/pricing.mjs — an indicative figure derived from the pack size and weight
 *      stated in the listing title. Plausible and consistent, but not a real supplier price.
 *
 * Pass --no-estimates to skip step 2 and leave anything uncovered by the CSV at zero, which the
 * storefront renders as "price on request".
 *
 * An .xlsx is a zip of XML, so this reads it directly instead of pulling in a parser: the
 * dependency would exist only for this one script, run by hand a handful of times.
 */

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { indicativePrice, indicativeStock } from './pricing.mjs';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const DEFAULT_SOURCE = resolve(workspaceRoot, 'tools/data/Gaushala_Amazon_Products_List.xlsx');
const OUTPUT = resolve(workspaceRoot, 'tools/data/catalog.json');
const PRICES = resolve(workspaceRoot, 'tools/data/prices.csv');

/** Fixed so a re-import produces the same ids and the same URLs keep working. */
const NAMESPACE = 'upbazaar.catalog.gaushala';
const SELLER_ID = '22222222-2222-2222-2222-222222222222';
const CURRENCY = 'INR';

/**
 * Short codes for the SKUs. Keyed on the workbook's category label so an unrecognised category
 * fails loudly rather than silently sharing a prefix with another.
 */
const CATEGORY_CODES = {
  'Gobar Agarbatti / Dhoop Batti': 'AGB',
  'Gobar Kande / Upale (Cow Dung Cakes)': 'KAN',
  'Gomutra Ark (Distilled Cow Urine)': 'ARK',
  'Panchgavya / Gomutra Sabun (Soap)': 'SAB',
  'Gobar Diya / Deepak': 'DIY',
  'Gomutra Phenyl / Floor Cleaner': 'PHN',
  'Gobar Khad / Organic Manure': 'KHD',
  'Gobar Sambrani / Havan Cups': 'SAM',
  'Gomutra Ghanvati / Ayurvedic': 'GHN',
};

// --- workbook reading ------------------------------------------------------

/** Excel stores repeated text once and references it by index. */
function readSharedStrings(unzip) {
  const xml = unzip('xl/sharedStrings.xml');

  return [...xml.matchAll(/<si>(.*?)<\/si>/gs)]
    .map(([, si]) => [...si.matchAll(/<t[^>]*>(.*?)<\/t>/gs)].map((m) => m[1]).join(''))
    .map(decodeXml);
}

function decodeXml(value) {
  return value
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&');
}

function readRows(unzip, sheet, strings) {
  const xml = unzip(`xl/worksheets/${sheet}`);
  const rows = [];

  for (const [, , body] of xml.matchAll(/<row([^>]*)>(.*?)<\/row>/gs)) {
    const cells = {};

    for (const [, attrs, cell] of body.matchAll(/<c([^>]*)>(.*?)<\/c>/gs)) {
      const column = attrs.match(/r="([A-Z]+)\d+"/)?.[1];
      const type = attrs.match(/t="([^"]+)"/)?.[1];
      const raw = cell.match(/<v>(.*?)<\/v>/s)?.[1] ?? cell.match(/<t[^>]*>(.*?)<\/t>/s)?.[1] ?? '';

      cells[column] = type === 's' ? strings[Number(raw)] : decodeXml(raw);
    }

    rows.push(cells);
  }

  return rows;
}

/** PowerShell ships with .NET's zip reader, which saves shelling out to a third-party unzip. */
function makeUnzip(archivePath) {
  const script = `
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead('${archivePath.replace(/'/g, "''")}')
    try {
      $entry = $zip.Entries | Where-Object { $_.FullName -eq $env:ENTRY_NAME }
      if (-not $entry) { throw "no entry $env:ENTRY_NAME" }
      $reader = New-Object System.IO.StreamReader($entry.Open())
      try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
  `;

  return (entry) =>
    execFileSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', script], {
      encoding: 'utf8',
      maxBuffer: 32 * 1024 * 1024,
      env: { ...process.env, ENTRY_NAME: entry },
    });
}

// --- derivation ------------------------------------------------------------

/**
 * A name-based UUID, so importing twice yields the same catalogue rather than a second copy.
 * This is RFC 4122 version 5: SHA-1 of namespace + name, with the version and variant bits
 * overwritten.
 */
function deterministicId(...parts) {
  const digest = createHash('sha1').update(`${NAMESPACE}:${parts.join(':')}`).digest();

  digest[6] = (digest[6] & 0x0f) | 0x50;
  digest[8] = (digest[8] & 0x3f) | 0x80;

  const hex = digest.subarray(0, 16).toString('hex');

  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function slugify(value) {
  return value
    .toLowerCase()
    .replace(/&/g, ' and ')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80);
}

/**
 * Supplied prices, keyed by SKU: "sku,price,stock" with a header row. Blank or missing stock
 * leaves the estimate in place; a price of 0 explicitly means "not for sale yet".
 */
function readSuppliedPrices() {
  if (!existsSync(PRICES)) {
    return new Map();
  }

  const rows = readFileSync(PRICES, 'utf8')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '' && !line.startsWith('#'));

  const supplied = new Map();

  for (const [index, line] of rows.entries()) {
    const [sku, price, stock] = line.split(',').map((cell) => cell.trim());

    if (index === 0 && sku.toLowerCase() === 'sku') {
      continue;
    }

    if (!sku || price === undefined || Number.isNaN(Number(price))) {
      throw new Error(`${PRICES}: cannot read line "${line}". Expected "sku,price,stock".`);
    }

    supplied.set(sku, {
      price: Number(price),
      stock: stock === undefined || stock === '' ? null : Number(stock),
    });
  }

  return supplied;
}

function main() {
  const sourceFlag = process.argv.indexOf('--source');
  const estimate = !process.argv.includes('--no-estimates');
  const supplied = readSuppliedPrices();
  const source = sourceFlag === -1 ? DEFAULT_SOURCE : resolve(process.argv[sourceFlag + 1]);

  const unzip = makeUnzip(source);
  const strings = readSharedStrings(unzip);

  const productRows = readRows(unzip, 'sheet1.xml', strings).slice(1).filter((r) => r.C);
  const summaryRows = readRows(unzip, 'sheet2.xml', strings).slice(1);

  const notes = new Map(
    summaryRows.filter((r) => r.A && r.C && r.A !== 'TOTAL').map((r) => [r.A, r.C])
  );

  const categories = [];
  const seenCategories = new Map();
  const products = [];
  const perCategoryCount = new Map();

  for (const row of productRows) {
    const categoryName = row.B;
    const code = CATEGORY_CODES[categoryName];

    if (!code) {
      throw new Error(`Unmapped category "${categoryName}". Add it to CATEGORY_CODES.`);
    }

    let category = seenCategories.get(categoryName);

    if (!category) {
      category = {
        id: deterministicId('category', categoryName),
        name: categoryName,
        slug: slugify(categoryName),
        parentId: null,
        code,
        note: notes.get(categoryName) ?? null,
      };

      seenCategories.set(categoryName, category);
      categories.push(category);
    }

    const index = (perCategoryCount.get(code) ?? 0) + 1;
    perCategoryCount.set(code, index);

    const name = row.C;
    const sku = `UPB-${code}-${String(index).padStart(3, '0')}`;

    const fromCsv = supplied.get(sku);

    const price = fromCsv ? fromCsv.price : estimate ? indicativePrice(sku, name) : 0;

    const onHand = fromCsv?.stock ?? (estimate ? indicativeStock(sku) : 0);

    products.push({
      id: deterministicId('product', sku),
      sku,
      name,
      slug: slugify(name),
      brand: row.D || null,
      // The workbook carries no marketing copy. Leaving this null is honest; the product page
      // shows the brand and category panel instead of a paragraph of invented prose.
      description: null,
      price,
      // Bookkeeping, not part of the API contract: which of these numbers anyone should believe.
      priceSource: fromCsv ? 'supplied' : estimate ? 'estimated' : 'none',
      currency: CURRENCY,
      status: 'Active',
      sellerId: SELLER_ID,
      categoryId: category.id,
      onHandQuantity: onHand,
      reservedQuantity: 0,
      createdAtUtc: '2026-08-24T00:00:00Z',
      modifiedAtUtc: null,
    });
  }

  const estimated = products.filter((product) => product.priceSource === 'estimated').length;
  const fromCsvCount = products.filter((product) => product.priceSource === 'supplied').length;
  const unpriced = products.filter((product) => product.priceSource === 'none').length;

  const document = {
    $comment:
      'Generated by tools/scripts/import-catalog.mjs from tools/data/Gaushala_Amazon_Products_List.xlsx. Do not hand-edit; re-run the importer.',
    source: 'Gaushala_Amazon_Products_List.xlsx',
    generatedFrom: 'Amazon.in listing survey, 24-Aug-2026',
    priceWarning:
      estimated > 0
        ? `${estimated} of ${products.length} prices are INDICATIVE estimates from ` +
          'tools/scripts/pricing.mjs, not supplier prices. Put real figures in ' +
          'tools/data/prices.csv to override them.'
        : null,
    currency: CURRENCY,
    categories,
    products,
  };

  mkdirSync(dirname(OUTPUT), { recursive: true });
  writeFileSync(OUTPUT, `${JSON.stringify(document, null, 2)}\n`);

  process.stdout.write(
    `Wrote ${products.length} products in ${categories.length} categories to ${OUTPUT}\n`
  );

  if (estimated > 0) {
    process.stdout.write(
      `\n  WARNING: ${estimated} prices are INDICATIVE estimates, not supplier prices.\n` +
        '  They are derived from the pack size and weight in each listing title.\n' +
        '  Put real figures in tools/data/prices.csv to override them.\n\n'
    );
  }

  if (fromCsvCount > 0) {
    process.stdout.write(`  ${fromCsvCount} prices came from tools/data/prices.csv.\n`);
  }

  if (unpriced > 0) {
    process.stdout.write(`  ${unpriced} left unpriced; the storefront shows "price on request".\n`);
  }
}

main();
