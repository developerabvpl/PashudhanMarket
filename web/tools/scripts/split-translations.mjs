#!/usr/bin/env node
// Writes each app's own copy of the strings it uses, from the shared source.
//
//   npm run i18n:split            # rewrite apps/<app>/src/app/i18n/{en,hi}.json
//   npm run i18n:split -- --check # fail if any app's copy is out of date (run by each app's lint)
//
// libs/ui/src/lib/i18n/en.json and hi.json stay the one place strings are written. The apps' copies
// are generated: never edit them by hand, edit the source and run this.
//
// Why: English is in each app's first download so the server render and first paint have their
// words. The shared file holds every screen of all three apps, and a buyer on the storefront has
// no use for the admin portal's settlement screens.
//
// How a key is judged used: the app's source, and the shared libraries it builds on, are scanned
// for string literals that name a key ('orders.status.placed') or a prefix that ends in a dot
// ('orders.status.' or `orders.status.${status}`), which keeps that whole subtree. Every key in
// this codebase is written out somewhere in one of those two forms - in a template, a label array,
// a status map or a toast call - so the scan finds them without a list to maintain. A key put
// together any other way would be missed; keep keys as literals.

import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const SOURCE_DIR = resolve(workspaceRoot, 'libs/ui/src/lib/i18n');
const LANGUAGES = ['en', 'hi'];

/** The shared libraries every app builds on. The generated API client holds no strings. */
const SHARED_ROOTS = ['libs/ui/src', 'libs/auth/src', 'libs/util/src', 'libs/data-access/src'];
const IGNORED_DIRS = [resolve(workspaceRoot, 'libs/data-access/src/lib/api')];

const APPS = {
  storefront: {},
  'seller-portal': {},
  'admin-portal': {},
};

// Storefront never loads the portals' Material sign-in pages; their strings stay out of it.
APPS.storefront.ignore = [resolve(workspaceRoot, 'libs/auth/src/lib/portal')];

const KEY_LITERAL = /['"`]([A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*\.?)(?:['"`]|\$\{)/g;

function sourceFiles(dir, ignored) {
  if (ignored.includes(dir)) {
    return [];
  }

  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);

    if (entry.isDirectory()) {
      return sourceFiles(path, ignored);
    }

    return /\.(ts|html)$/.test(entry.name) && !/\.spec\.ts$/.test(entry.name) ? [path] : [];
  });
}

function referencedLiterals(app) {
  const ignored = [...IGNORED_DIRS, ...(APPS[app].ignore ?? [])];
  const roots = [`apps/${app}/src`, ...SHARED_ROOTS].map((root) => resolve(workspaceRoot, root));
  const literals = new Set();

  for (const file of roots.flatMap((root) => sourceFiles(root, ignored))) {
    for (const match of readFileSync(file, 'utf8').matchAll(KEY_LITERAL)) {
      literals.add(match[1]);
    }
  }

  return literals;
}

/**
 * Copies the parts of the tree the literals reach, keeping the source's key order: a leaf named
 * in full, or anything under a prefix. A literal naming only a namespace ('nav') is an ordinary
 * word far more often than a key, so it keeps nothing.
 */
function prune(tree, literals, prefix = '') {
  const prefixes = [...literals].filter((literal) => literal.endsWith('.'));
  const underPrefix = (key) => prefixes.some((p) => key.startsWith(p));
  const kept = {};

  for (const [name, value] of Object.entries(tree)) {
    const key = prefix + name;

    if (typeof value === 'object') {
      const child = underPrefix(`${key}.`) ? value : prune(value, literals, `${key}.`);

      if (Object.keys(child).length > 0) {
        kept[name] = child;
      }
    } else if (literals.has(key) || underPrefix(key)) {
      kept[name] = value;
    }
  }

  return kept;
}

function countKeys(tree) {
  return Object.values(tree).reduce((sum, value) => sum + (typeof value === 'object' ? countKeys(value) : 1), 0);
}

const check = process.argv.includes('--check');
const onlyApp = process.argv.find((arg) => arg.startsWith('--app='))?.slice('--app='.length);
const sources = Object.fromEntries(
  LANGUAGES.map((lang) => [lang, JSON.parse(readFileSync(join(SOURCE_DIR, `${lang}.json`), 'utf8'))])
);
const stale = [];

for (const app of Object.keys(APPS).filter((name) => !onlyApp || name === onlyApp)) {
  const literals = referencedLiterals(app);
  const outDir = resolve(workspaceRoot, `apps/${app}/src/app/i18n`);

  for (const lang of LANGUAGES) {
    const pruned = prune(sources[lang], literals);
    const text = `${JSON.stringify(pruned, null, 2)}\n`;
    const outFile = join(outDir, `${lang}.json`);

    if (check) {
      if (!existsSync(outFile) || readFileSync(outFile, 'utf8').replace(/\r\n/g, '\n') !== text) {
        stale.push(relative(workspaceRoot, outFile));
      }
    } else {
      mkdirSync(outDir, { recursive: true });
      writeFileSync(outFile, text);
      process.stdout.write(
        `${app} ${lang}: ${countKeys(pruned)} of ${countKeys(sources[lang])} keys, ` +
          `${(Buffer.byteLength(text) / 1024).toFixed(1)} kB\n`
      );
    }
  }
}

if (stale.length > 0) {
  process.stderr.write(
    `These translation files are out of date with libs/ui/src/lib/i18n:\n  ${stale.join('\n  ')}\n` +
      'Run npm run i18n:split and commit the result.\n'
  );
  process.exit(1);
}
