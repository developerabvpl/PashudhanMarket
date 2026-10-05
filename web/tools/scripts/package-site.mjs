#!/usr/bin/env node
/**
 * Assembles the whole site - storefront and both portals - into one directory for one domain.
 *
 *   npx nx run-many -t build -p storefront seller-portal admin-portal
 *   node tools/scripts/package-site.mjs [--out dist/deploy]
 *
 * The storefront is the site root; the seller portal goes in seller/ and the admin portal in
 * admin/, so one IIS website (or one nginx server block) serves /, /seller/ and /admin/, and
 * forwards /api to the .NET API.
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

import { chmodSync, cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const BUILD = resolve(workspaceRoot, 'dist/apps/storefront');

/**
 * The portals and the folder each is served from. The folder name is the URL path: the web
 * server's rules (tools/deploy/web.config, nginx.conf.example) name the same two.
 */
const PORTALS = [
  { project: 'seller-portal', path: 'seller' },
  { project: 'admin-portal', path: 'admin' },
];
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
  { from: 'tools/deploy/README.md', to: 'README.md' },
  { from: 'tools/deploy/web.config', to: 'web.config' },
  { from: 'tools/deploy/nginx.conf.example', to: 'nginx.conf.example' },
];

function parseOut(argv) {
  const flag = argv.indexOf('--out');

  return resolve(workspaceRoot, flag === -1 ? 'dist/deploy' : argv[flag + 1]);
}

function portalBuild(portal) {
  return resolve(workspaceRoot, 'dist/apps', portal.project, 'browser');
}

/**
 * Points a portal's page at the folder it is served from.
 *
 * A portal is built for a site root, which is how it runs in development and how it would run on
 * a domain of its own. Everything it loads - scripts, styles, lazy chunks, fonts - it names
 * relative to the page's base, and the router reads its base from the same tag, so changing this
 * one tag is all that moving it under /seller/ or /admin/ takes. Done here rather than at build
 * time so the same build serves either way.
 */
function serveFrom(indexFile, basePath) {
  const html = readFileSync(indexFile, 'utf8');
  const rebased = html.replace(/<base href="\/"\s*\/?>/, `<base href="${basePath}">`);

  if (rebased === html) {
    throw new Error(`${indexFile} has no <base href="/"> to point at ${basePath}.`);
  }

  writeFileSync(indexFile, rebased);
}

function main() {
  const out = parseOut(process.argv);

  if (!existsSync(join(BUILD, 'browser/index.html'))) {
    throw new Error(`No build at ${BUILD}. Run "npx nx build storefront" first.`);
  }

  for (const portal of PORTALS) {
    if (!existsSync(join(portalBuild(portal), 'index.html'))) {
      throw new Error(`No build of ${portal.project}. Run "npx nx build ${portal.project}" first.`);
    }
  }

  if (!existsSync(DEPLOY)) {
    throw new Error(`Missing ${DEPLOY}, which holds the host configuration.`);
  }

  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });

  cpSync(join(BUILD, 'browser'), out, { recursive: true });

  for (const portal of PORTALS) {
    if (existsSync(join(out, portal.path))) {
      // A storefront route of the same name would be shadowed by the portal's folder.
      throw new Error(`The storefront build already has a "${portal.path}" folder.`);
    }

    cpSync(portalBuild(portal), join(out, portal.path), { recursive: true });
    serveFrom(join(out, portal.path, 'index.html'), `/${portal.path}/`);
  }

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

  const catalog = JSON.parse(
    readFileSync(resolve(workspaceRoot, 'tools/data/catalog.json'), 'utf8')
  );
  const pages = readdirSync(join(out, 'products'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory()).length;

  process.stdout.write(
    `Packaged the site into ${out}\n` +
      `  /         storefront: ${catalog.products.length} products, ${catalog.categories.length} categories, ` +
      `${pages} prerendered product pages\n` +
      PORTALS.map((portal) => `  /${portal.path}/`.padEnd(12) + `${portal.project}\n`).join('') +
      '  IIS: point a site at this folder. Nothing to run, nothing to convert to an application.\n' +
      '  The only module required is URL Rewrite; web.config does the rest.\n' +
      "  Full instructions are in the bundle's README.md\n"
  );
}

main();
