#!/usr/bin/env node
// Regenerates libs/data-access from the API contract.
//
//   npm run gen:api                      # from ../openapi.json (committed by the API repo)
//   npm run gen:api -- --url http://...  # from a running API, e.g. after changing an endpoint
//   API_URL=http://localhost:5199 npm run gen:api
//
// Nothing under libs/data-access/src/lib/api is hand-edited: this script owns that folder.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

/** The API repository sits one level up; this is the artifact it publishes. */
const CONTRACT_FILE = resolve(workspaceRoot, '../openapi.json');
const GENERATED_DIR = resolve(workspaceRoot, 'libs/data-access/src/lib/api');
const TEMP_CONTRACT = resolve(workspaceRoot, 'node_modules/.cache/openapi.fetched.json');
const DEFAULT_DOCUMENT_PATH = '/openapi/v1.json';

function parseUrl(argv) {
  const flagIndex = argv.indexOf('--url');
  const fromFlag = flagIndex !== -1 ? argv[flagIndex + 1] : undefined;
  const raw = fromFlag ?? process.env.API_URL;

  if (!raw) {
    return undefined;
  }

  // Accept either the API origin or the full document URL.
  return raw.includes(DEFAULT_DOCUMENT_PATH)
    ? raw
    : `${raw.replace(/\/+$/, '')}${DEFAULT_DOCUMENT_PATH}`;
}

async function resolveContract() {
  const url = parseUrl(process.argv.slice(2));

  if (url) {
    process.stdout.write(`Fetching contract from ${url}\n`);

    const response = await fetch(url);

    if (!response.ok) {
      throw new Error(`${url} responded ${response.status} ${response.statusText}`);
    }

    const document = await response.text();
    mkdirSync(dirname(TEMP_CONTRACT), { recursive: true });
    writeFileSync(TEMP_CONTRACT, document);

    return TEMP_CONTRACT;
  }

  if (!existsSync(CONTRACT_FILE)) {
    throw new Error(
      `No contract found at ${CONTRACT_FILE}.\n` +
        'Either run the API and pass --url http://localhost:5199, or refresh the committed ' +
        'artifact with: curl -s http://localhost:5199/openapi/v1.json -o openapi.json'
    );
  }

  return CONTRACT_FILE;
}

function describe(contractPath) {
  const document = JSON.parse(readFileSync(contractPath, 'utf8'));
  const paths = Object.keys(document.paths ?? {}).length;
  const schemas = Object.keys(document.components?.schemas ?? {}).length;

  process.stdout.write(`Contract: ${paths} paths, ${schemas} schemas (OpenAPI ${document.openapi})\n`);
}

const contractPath = await resolveContract();
describe(contractPath);

// Wipe first so a deleted endpoint disappears instead of lingering as a stale service.
rmSync(GENERATED_DIR, { recursive: true, force: true });

// Resolve the generator's entry point and run it on this Node, rather than shelling out to
// npx: spawning a .cmd shim fails outright on Windows.
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
  [resolveGeneratorBin(), '--input', contractPath, '--output', GENERATED_DIR],
  { cwd: workspaceRoot, stdio: 'inherit' }
);

process.stdout.write(`Generated client in ${GENERATED_DIR}\n`);
