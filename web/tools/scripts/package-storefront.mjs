#!/usr/bin/env node
/**
 * Assembles everything the storefront needs on a server into one directory.
 *
 *   npx nx build storefront
 *   node tools/scripts/package-storefront.mjs [--out dist/deploy]
 *
 * The Angular build alone is not deployable. It leaves out the catalogue stub and the JSON it
 * serves, which live under tools/ and are where all seventy products come from, and it says
 * nothing about the reverse proxy the browser needs in order to reach the API on its own origin.
 * This copies the first two and brings the host configuration along with them.
 *
 * The result is self-contained: it can be moved anywhere, and nothing in it points back at this
 * repository.
 *
 * Everything shipped alongside the build is a real file under tools/deploy rather than a string
 * in here. Host configuration is full of backslashes, dollar signs and backticks, and embedding
 * it produced a start script that ran "serverserver.mjs" — the kind of mistake that only shows
 * up on the server.
 */

import { chmodSync, cpSync, existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const BUILD = resolve(workspaceRoot, 'dist/apps/storefront');
const DEPLOY = resolve(workspaceRoot, 'tools/deploy');

/**
 * What to copy where. The stub's own layout has to be mirrored: it resolves its data as
 * ../../tools/data relative to its own file.
 */
const FILES = [
  { from: 'tools/scripts/stub-api.mjs', to: 'tools/scripts/stub-api.mjs' },
  { from: 'tools/data/catalog.json', to: 'tools/data/catalog.json' },
  { from: 'tools/deploy/README.md', to: 'README.md' },
  { from: 'tools/deploy/start.sh', to: 'start.sh', executable: true },
  { from: 'tools/deploy/start.cmd', to: 'start.cmd' },
  { from: 'tools/deploy/nginx.conf.example', to: 'nginx.conf.example' },
  { from: 'tools/deploy/web.config', to: 'web.config' },
  // The stub runs as its own IIS application rooted at tools/, so its config belongs there.
  { from: 'tools/deploy/tools.web.config', to: 'tools/web.config' },
];

function parseOut(argv) {
  const flag = argv.indexOf('--out');

  return resolve(workspaceRoot, flag === -1 ? 'dist/deploy' : argv[flag + 1]);
}

function main() {
  const out = parseOut(process.argv);

  if (!existsSync(join(BUILD, 'server/server.mjs'))) {
    throw new Error(`No build at ${BUILD}. Run "npx nx build storefront" first.`);
  }

  if (!existsSync(DEPLOY)) {
    throw new Error(`Missing ${DEPLOY}, which holds the host configuration.`);
  }

  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });

  cpSync(join(BUILD, 'browser'), join(out, 'browser'), { recursive: true });
  cpSync(join(BUILD, 'server'), join(out, 'server'), { recursive: true });

  for (const file of FILES) {
    const target = join(out, file.to);

    mkdirSync(dirname(target), { recursive: true });
    cpSync(resolve(workspaceRoot, file.from), target);

    // cpSync's own `mode` is a set of copy flags, not permissions. Packaging on Windows makes
    // this a no-op, but packaging on Linux should still produce a runnable start.sh.
    if (file.executable) {
      chmodSync(target, 0o755);
    }
  }

  const catalog = JSON.parse(readFileSync(join(out, 'tools/data/catalog.json'), 'utf8'));

  process.stdout.write(
    `Packaged the storefront into ${out}\n` +
      `  ${catalog.products.length} products, ${catalog.categories.length} categories\n` +
      '  IIS: point a site at this folder, then add an "api/catalog" application at tools\n' +
      '  Set NG_ALLOWED_HOSTS in web.config first: until you do, every request answers 400\n' +
      "  Full instructions are in the bundle's README.md\n"
  );
}

main();
