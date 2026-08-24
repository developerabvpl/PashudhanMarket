#!/usr/bin/env node
// Regenerates libs/data-access from the API contract.
//
//   npm run gen:api                                  # from the live API on the default port
//   npm run gen:api -- --url http://localhost:5199   # from a specific host
//   API_URL=http://localhost:5199 npm run gen:api
//   npm run gen:api -- --offline                     # from the committed ../openapi.json only
//
// Nothing under libs/data-access/src/lib/api is hand-edited: this script owns that folder.
//
// TRANSITIONAL: the live API currently exposes Identity only. The storefront's product pages
// and the portals' order and product screens were written against the previous design, whose
// contract is committed at ../openapi.json and is served in development by
// tools/scripts/stub-api.mjs. Until those modules are rebuilt, this script merges the two
// documents so every screen keeps a typed client. Live definitions always win; the archived
// ones only fill gaps. Delete `mergeArchivedContract` and the archived file the day Catalog,
// Orders, Payments and Shipping exist for real.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

/** The API repository sits one level up; this is the archived artifact it published. */
const ARCHIVED_CONTRACT = resolve(workspaceRoot, '../openapi.json');
const GENERATED_DIR = resolve(workspaceRoot, 'libs/data-access/src/lib/api');
const MERGED_CONTRACT = resolve(workspaceRoot, 'node_modules/.cache/openapi.merged.json');
const DEFAULT_API_URL = 'http://localhost:5199';
const DOCUMENT_PATH = '/openapi/v1.json';

function parseUrl(argv) {
  if (argv.includes('--offline')) {
    return undefined;
  }

  const flagIndex = argv.indexOf('--url');
  const raw = (flagIndex === -1 ? undefined : argv[flagIndex + 1]) ?? process.env.API_URL ?? DEFAULT_API_URL;

  return raw.includes(DOCUMENT_PATH) ? raw : `${raw.replace(/\/+$/, '')}${DOCUMENT_PATH}`;
}

async function fetchLiveDocument(url) {
  process.stdout.write(`Fetching live contract from ${url}\n`);

  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(`${url} responded ${response.status} ${response.statusText}`);
  }

  return await response.json();
}

function readArchivedDocument() {
  if (!existsSync(ARCHIVED_CONTRACT)) {
    return undefined;
  }

  return JSON.parse(readFileSync(ARCHIVED_CONTRACT, 'utf8'));
}

/**
 * Fills gaps in the live document from the archived one. A path or schema that exists in both
 * keeps the live definition, so the running API is always the authority.
 */
function mergeArchivedContract(live, archived) {
  if (!archived) {
    return { document: live, addedPaths: 0 };
  }

  const document = structuredClone(live);
  document.paths ??= {};
  document.components ??= {};
  document.components.schemas ??= {};

  let addedPaths = 0;

  for (const [path, item] of Object.entries(archived.paths ?? {})) {
    if (!document.paths[path]) {
      document.paths[path] = item;
      addedPaths++;
    }
  }

  for (const [name, schema] of Object.entries(archived.components?.schemas ?? {})) {
    document.components.schemas[name] ??= schema;
  }

  return { document, addedPaths };
}

const url = parseUrl(process.argv.slice(2));
const archived = readArchivedDocument();

let live;

if (url) {
  try {
    live = await fetchLiveDocument(url);
  } catch (error) {
    throw new Error(
      `Could not read the live contract: ${error.message}\n` +
        'Start the API (dotnet run --project ../src/UPBazaar.Api) or pass --offline to ' +
        'generate from the committed contract alone.',
      { cause: error }
    );
  }
} else {
  process.stdout.write('Offline: generating from the committed contract only.\n');

  if (!archived) {
    throw new Error(`No contract found at ${ARCHIVED_CONTRACT}.`);
  }

  live = archived;
}

const { document, addedPaths } = url ? mergeArchivedContract(live, archived) : { document: live, addedPaths: 0 };

const pathCount = Object.keys(document.paths ?? {}).length;
const schemaCount = Object.keys(document.components?.schemas ?? {}).length;

process.stdout.write(
  `Contract: ${pathCount} paths, ${schemaCount} schemas` +
    (addedPaths > 0 ? ` (${addedPaths} carried over from the archived contract)` : '') +
    '\n'
);

mkdirSync(dirname(MERGED_CONTRACT), { recursive: true });
writeFileSync(MERGED_CONTRACT, JSON.stringify(document, null, 2));

// Wipe first so a removed endpoint disappears instead of lingering as a stale operation.
rmSync(GENERATED_DIR, { recursive: true, force: true });

/**
 * Resolves the generator's entry point and runs it on this Node, rather than shelling out to
 * npx: spawning a .cmd shim fails outright on Windows.
 */
function resolveGeneratorBin() {
  const packageJsonPath = resolve(workspaceRoot, 'node_modules/ng-openapi-gen/package.json');

  if (!existsSync(packageJsonPath)) {
    throw new Error('ng-openapi-gen is not installed. Run npm install first.');
  }

  const { bin } = JSON.parse(readFileSync(packageJsonPath, 'utf8'));
  const entry = typeof bin === 'string' ? bin : bin['ng-openapi-gen'];

  return resolve(dirname(packageJsonPath), entry);
}

execFileSync(
  process.execPath,
  [resolveGeneratorBin(), '--input', MERGED_CONTRACT, '--output', GENERATED_DIR],
  { cwd: workspaceRoot, stdio: 'inherit' }
);

process.stdout.write(`Generated client in ${GENERATED_DIR}\n`);
