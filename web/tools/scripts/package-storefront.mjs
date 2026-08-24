#!/usr/bin/env node
/**
 * Assembles everything the storefront needs on a server into one directory.
 *
 *   npx nx build storefront
 *   node tools/scripts/package-storefront.mjs [--out dist/deploy]
 *
 * The Angular build alone is not enough to run. It leaves out the catalogue stub and the JSON it
 * serves, which live under tools/ and are what the product pages actually read, and it says
 * nothing about the reverse proxy the browser needs in order to reach the API on its own origin.
 * This copies the first two and writes the third down beside them.
 *
 * The result is self-contained: it can be moved anywhere, and nothing in it points back at this
 * repository.
 */

import { cpSync, existsSync, mkdirSync, rmSync, writeFileSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const BUILD = resolve(workspaceRoot, 'dist/apps/storefront');

function parseOut(argv) {
  const flag = argv.indexOf('--out');

  return resolve(workspaceRoot, flag === -1 ? 'dist/deploy' : argv[flag + 1]);
}

const START_SH = `#!/usr/bin/env bash
# Starts the catalogue stub and the storefront. Put a reverse proxy in front of both; see README.
set -euo pipefail

: "\${NG_ALLOWED_HOSTS:?set this to the public hostname, comma separated}"
: "\${PORT:=4000}"
: "\${STUB_API_PORT:=5200}"

node tools/scripts/stub-api.mjs --port "\$STUB_API_PORT" &
STUB=\$!
trap 'kill \$STUB 2>/dev/null || true' EXIT

SSR_API_ORIGIN="http://127.0.0.1:\${STUB_API_PORT}" PORT="\$PORT" \\
  node server/server.mjs
`;

const README = `# UP Bazaar storefront — deployment bundle

Built from the UP Bazaar workspace by \`tools/scripts/package-storefront.mjs\`. Self-contained:
copy the whole directory to the server and run it.

    NG_ALLOWED_HOSTS=upbazaar.example,www.upbazaar.example ./start.sh

## What is in here

    browser/              static assets, including the product photographs
    server/               the Angular SSR server (Node, Express)
    tools/scripts/        the catalogue stub
    tools/data/           catalog.json, the 70 products it serves
    start.sh              starts both

## The three things that are easy to get wrong

**NG_ALLOWED_HOSTS is required.** Angular rejects any request whose \`Host\` header is not listed,
and an unset list means every request is answered with 400 — the site refusing its own visitors.
Set it to the public hostname. Do not set it to \`*\` unless the proxy in front is already
validating the header.

**A reverse proxy is required.** The SSR server does not proxy \`/api\`, and the browser calls the
API on its own origin. Without one, the first paint is correct and the page then empties as soon
as it hydrates and refetches. One public origin, three upstreams:

| Path | Upstream |
| --- | --- |
| \`/api/catalog/**\` | the catalogue stub (STUB_API_PORT, default 5200) |
| \`/api/**\` | the .NET API, if you are running one |
| everything else | the SSR server (PORT, default 4000) |

nginx:

    location /api/catalog/ { proxy_pass http://127.0.0.1:5200; }
    location /api/         { proxy_pass http://127.0.0.1:5199; }
    location /             { proxy_pass http://127.0.0.1:4000;
                             proxy_set_header Host $host; }

**The .NET API is optional, and it needs SQL Server.** Everything a shopper does — browsing,
search, category filters, product pages, the basket — comes from the stub and the browser's own
storage, and works with no API and no database at all. Only sign-in needs the API, and the API
does not start without its database: it applies migrations and initialises Hangfire's SQL storage
before it listens. Leave \`/api/**\` unrouted and the storefront still runs; the sign-in panel
reports a failure instead of breaking the page.

## Prices are indicative

The catalogue was imported from a listing survey with no price column. Every price in
\`tools/data/catalog.json\` is an estimate derived from the pack size in the listing title, not a
supplier price. Replace them before anyone treats this as a price list.
`;

function main() {
  const out = parseOut(process.argv);

  if (!existsSync(join(BUILD, 'server/server.mjs'))) {
    throw new Error(`No build at ${BUILD}. Run "npx nx build storefront" first.`);
  }

  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });

  cpSync(join(BUILD, 'browser'), join(out, 'browser'), { recursive: true });
  cpSync(join(BUILD, 'server'), join(out, 'server'), { recursive: true });

  // The stub resolves its data as ../../tools/data from its own location, so this layout has to
  // mirror the workspace's.
  mkdirSync(join(out, 'tools/scripts'), { recursive: true });
  mkdirSync(join(out, 'tools/data'), { recursive: true });
  cpSync(
    resolve(workspaceRoot, 'tools/scripts/stub-api.mjs'),
    join(out, 'tools/scripts/stub-api.mjs')
  );
  cpSync(resolve(workspaceRoot, 'tools/data/catalog.json'), join(out, 'tools/data/catalog.json'));

  writeFileSync(join(out, 'start.sh'), START_SH, { mode: 0o755 });
  writeFileSync(join(out, 'README.md'), README);

  const catalog = JSON.parse(readFileSync(join(out, 'tools/data/catalog.json'), 'utf8'));

  process.stdout.write(
    `Packaged the storefront into ${out}\n` +
      `  ${catalog.products.length} products, ${catalog.categories.length} categories\n` +
      `  start with: NG_ALLOWED_HOSTS=<your hostname> ./start.sh\n` +
      `  a reverse proxy in front is not optional; see the bundled README\n`
  );
}

main();
