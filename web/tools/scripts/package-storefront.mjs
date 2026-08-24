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

import { chmodSync, cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const BUILD = resolve(workspaceRoot, 'dist/apps/storefront');
const DEPLOY = resolve(workspaceRoot, 'tools/deploy');

/**
 * What to copy where.
 *
 * The stub lands at api/catalog because that is the URL it answers on, and IIS Manager will only
 * accept a single path segment as an application alias — no "api/catalog". With the folders laid
 * out to match, adding the application is a right-click on `catalog` and the alias is just
 * `catalog`, which lands it at /api/catalog with nothing to configure.
 *
 * It keeps working there because it resolves its data two levels up from its own file, and
 * api/catalog is the same depth as the tools/scripts it used to sit in.
 */
const FILES = [
  { from: 'tools/scripts/stub-api.mjs', to: 'api/catalog/stub-api.mjs' },
  { from: 'tools/deploy/catalog.web.config', to: 'api/catalog/web.config' },
  { from: 'tools/data/catalog.json', to: 'tools/data/catalog.json' },
  { from: 'tools/deploy/README.md', to: 'README.md' },
  { from: 'tools/deploy/start.sh', to: 'start.sh', executable: true },
  { from: 'tools/deploy/start.cmd', to: 'start.cmd', crlf: true },
  { from: 'tools/deploy/nginx.conf.example', to: 'nginx.conf.example' },
  { from: 'tools/deploy/web.config', to: 'web.config' },
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

    if (file.crlf) {
      // A batch file with LF-only line endings is read wrong by cmd.exe — it split "REM" and
      // reported that 'M' is not a recognised command. git may well have normalised the source
      // to LF on the way in, so convert here rather than trusting what is on disk.
      const text = readFileSync(resolve(workspaceRoot, file.from), 'utf8');

      writeFileSync(target, text.replace(/\r?\n/g, '\r\n'));
    } else {
      cpSync(resolve(workspaceRoot, file.from), target);
    }

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
      '  IIS: point a site at this folder, then right-click api\\catalog and Convert to Application\n' +
      '  Set NG_ALLOWED_HOSTS in web.config first: until you do, every request answers 400\n' +
      "  Full instructions are in the bundle's README.md\n"
  );
}

main();
