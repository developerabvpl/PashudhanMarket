#!/usr/bin/env node
/**
 * Stand-in for the UP Bazaar API, used by the storefront smoke suite.
 *
 * The storefront renders on the server, so its data fetch happens in Node where Playwright's
 * page.route() cannot reach. Browser-side mocking would therefore only cover the hydrated
 * page and leave the server render — the part that matters for SEO — untested. A real HTTP
 * stub covers both, and its responses are shaped by the same openapi.json the client is
 * generated from.
 *
 *   node tools/scripts/stub-api.mjs [--port 5199]
 */

import { createServer } from 'node:http';

const portFlag = process.argv.indexOf('--port');
const port = Number(portFlag === -1 ? process.env.STUB_API_PORT ?? 5200 : process.argv[portFlag + 1]);

export const PRODUCT_ID = '11111111-1111-1111-1111-111111111111';
export const ORDER_ID = '44444444-4444-4444-4444-444444444444';

const product = {
  id: PRODUCT_ID,
  sku: 'UPB-SAREE-001',
  name: 'Banarasi Silk Saree',
  slug: 'banarasi-silk-saree',
  description: 'Handwoven silk saree from Varanasi.',
  price: 4599,
  currency: 'INR',
  status: 'Active',
  sellerId: '22222222-2222-2222-2222-222222222222',
  category: {
    id: '33333333-3333-3333-3333-333333333333',
    name: 'Sarees',
    slug: 'sarees',
    parentId: null,
  },
  onHandQuantity: 10,
  reservedQuantity: 2,
  createdAtUtc: '2026-03-14T10:00:00Z',
  modifiedAtUtc: null,
};

const summary = {
  id: product.id,
  sku: product.sku,
  name: product.name,
  price: product.price,
  currency: product.currency,
  status: product.status,
  availableQuantity: product.onHandQuantity - product.reservedQuantity,
};

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

  if (path === '/api/catalog/products' && request.method === 'GET') {
    // "nothing" is the agreed search term for exercising the empty state.
    const empty = url.searchParams.get('Search') === 'nothing';

    return json(response, 200, {
      items: empty ? [] : [summary],
      page: 1,
      pageSize: 24,
      totalCount: empty ? 0 : 1,
      totalPages: empty ? 0 : 1,
      hasNextPage: false,
    });
  }

  if (path === `/api/catalog/products/${PRODUCT_ID}` && request.method === 'GET') {
    return json(response, 200, product);
  }

  if (path.startsWith('/api/catalog/products/') && request.method === 'GET') {
    return json(response, 404, {
      type: 'https://upbazaar.dev/errors/catalog.product.not_found',
      title: 'The product does not exist.',
      status: 404,
    });
  }

  return json(response, 404, { title: 'No stub for this route.', status: 404 });
});

server.listen(port, () => {
  process.stdout.write(`Stub API listening on http://localhost:${port}\n`);
});
