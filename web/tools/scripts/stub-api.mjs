#!/usr/bin/env node
/**
 * Stand-in for the UP Bazaar Catalog module, used in development and by the storefront smoke
 * suite.
 *
 * The storefront renders on the server, so its data fetch happens in Node where Playwright's
 * page.route() cannot reach. Browser-side mocking would therefore only cover the hydrated
 * page and leave the server render — the part that matters for SEO — untested. A real HTTP
 * stub covers both, and its responses are shaped by the same openapi.json the client is
 * generated from.
 *
 * The catalogue it serves is tools/data/catalog.json, produced by
 * tools/scripts/import-catalog.mjs from the Gaushala product workbook.
 *
 *   node tools/scripts/stub-api.mjs [--port 5200]
 */

import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

const portFlag = process.argv.indexOf('--port');
const port = Number(portFlag === -1 ? process.env.STUB_API_PORT ?? 5200 : process.argv[portFlag + 1]);

const catalog = JSON.parse(readFileSync(resolve(workspaceRoot, 'tools/data/catalog.json'), 'utf8'));

const categoriesById = new Map(catalog.categories.map((c) => [c.id, c]));

/** The contract's CategoryDto; `code` and `note` are import bookkeeping and stay out of it. */
function categoryDto(category) {
  return {
    id: category.id,
    name: category.name,
    slug: category.slug,
    parentId: category.parentId,
  };
}

function productDto(product) {
  return {
    id: product.id,
    sku: product.sku,
    name: product.name,
    slug: product.slug,
    brand: product.brand,
    description: product.description,
    price: product.price,
    currency: product.currency,
    status: product.status,
    sellerId: product.sellerId,
    category: categoryDto(categoriesById.get(product.categoryId)),
    onHandQuantity: product.onHandQuantity,
    reservedQuantity: product.reservedQuantity,
    createdAtUtc: product.createdAtUtc,
    modifiedAtUtc: product.modifiedAtUtc,
  };
}

function summaryDto(product) {
  return {
    id: product.id,
    sku: product.sku,
    name: product.name,
    price: product.price,
    currency: product.currency,
    status: product.status,
    availableQuantity: product.onHandQuantity - product.reservedQuantity,
  };
}

/** Exported so the e2e suite can address a product without hardcoding an id. */
export const PRODUCT_ID = catalog.products[0].id;
export const ORDER_ID = '44444444-4444-4444-4444-444444444444';

const productsById = new Map(catalog.products.map((p) => [p.id, p]));

function search(params) {
  const term = (params.get('Search') ?? '').trim().toLowerCase();
  const categoryId = params.get('CategoryId');
  const activeOnly = params.get('ActiveOnly') === 'true';

  return catalog.products.filter((product) => {
    if (activeOnly && product.status !== 'Active') {
      return false;
    }

    if (categoryId && product.categoryId !== categoryId) {
      return false;
    }

    if (term === '') {
      return true;
    }

    // Brand and category are searchable too: "Goseva" and "diya" are how a shopper thinks
    // about this catalogue, and neither is in the SKU.
    const haystack = [
      product.name,
      product.sku,
      product.brand ?? '',
      categoriesById.get(product.categoryId)?.name ?? '',
    ]
      .join(' ')
      .toLowerCase();

    return haystack.includes(term);
  });
}

function json(response, status, body) {
  const payload = JSON.stringify(body);

  response.writeHead(status, {
    'content-type': status === 200 || status === 201 ? 'application/json' : 'application/problem+json',
    'content-length': Buffer.byteLength(payload),
    'access-control-allow-origin': '*',
  });
  response.end(payload);
}

const server = createServer((request, response) => {
  const url = new URL(request.url ?? '/', `http://localhost:${port}`);
  const path = url.pathname;

  if (path === '/api/catalog/categories' && request.method === 'GET') {
    return json(response, 200, catalog.categories.map(categoryDto));
  }

  if (path === '/api/catalog/products' && request.method === 'GET') {
    const params = url.searchParams;
    const page = Math.max(1, Number(params.get('Page') ?? 1));
    const pageSize = Math.min(100, Math.max(1, Number(params.get('PageSize') ?? 24)));

    const matches = search(params);
    const totalPages = Math.ceil(matches.length / pageSize);
    const start = (page - 1) * pageSize;

    return json(response, 200, {
      items: matches.slice(start, start + pageSize).map(summaryDto),
      page,
      pageSize,
      totalCount: matches.length,
      totalPages,
      hasNextPage: page < totalPages,
    });
  }

  if (path.startsWith('/api/catalog/products/') && request.method === 'GET') {
    const product = productsById.get(path.slice('/api/catalog/products/'.length));

    if (product) {
      return json(response, 200, productDto(product));
    }

    return json(response, 404, {
      type: 'https://upbazaar.dev/errors/catalog.product.not_found',
      title: 'The product does not exist.',
      status: 404,
    });
  }

  return json(response, 404, { title: 'No stub for this route.', status: 404 });
});

server.listen(port, () => {
  process.stdout.write(
    `Stub API listening on http://localhost:${port} ` +
      `(${catalog.products.length} products, ${catalog.categories.length} categories)\n`
  );
});
