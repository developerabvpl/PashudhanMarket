// Makes each generated endpoint function free of side effects, so bundles keep only the ones
// they call.
//
// ng-openapi-gen gives every function a PATH property, set by a top-level statement after it:
//
//   export function apiV1OrdersGet(...) { ... new RequestBuilder(rootUrl, apiV1OrdersGet.PATH, 'get') ... }
//   apiV1OrdersGet.PATH = '/api/v1/orders';
//
// That assignment is a side effect a bundler cannot prove harmless, so every module the barrel
// re-exports was kept - all of them, in every app's first download. Nothing reads PATH except the
// function itself, so the path is written into the call and the assignment dropped.
import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

function* files(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);

    if (statSync(path).isDirectory()) {
      yield* files(path);
    } else if (path.endsWith('.ts')) {
      yield path;
    }
  }
}

/** Rewrites every function file under <functionsDir>; returns how many it changed. */
export function inlineApiPaths(functionsDir) {
  let changed = 0;

  for (const file of files(functionsDir)) {
    const source = readFileSync(file, 'utf8');
    const newline = String.fromCharCode(10);
    const lines = source.split(newline);
    const index = lines.findIndex((l) => /^[A-Za-z0-9_]+[.]PATH = '[^']*';/.test(l.trim()));

    if (index === -1) {
      continue;
    }

    const [name, rest] = lines[index].trim().split('.PATH = ');
    const path = rest.slice(0, rest.lastIndexOf(';'));
    lines.splice(index, 1);
    const rewritten = lines.join(newline).split(`${name}.PATH`).join(path);

    writeFileSync(file, rewritten);
    changed++;
  }

  return changed;
}
