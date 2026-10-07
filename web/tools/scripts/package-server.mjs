#!/usr/bin/env node
/**
 * Puts the whole of UP Bazaar - API, storefront, seller portal and admin portal - into one
 * folder that one IIS website runs.
 *
 *   npx nx run-many -t build -p storefront seller-portal admin-portal
 *   node tools/scripts/package-site.mjs
 *   node tools/scripts/package-server.mjs [--out ../publish/upbazaar]
 *
 * package-site.mjs lays the three applications out as one site; this publishes the API and puts
 * that site in its wwwroot, where the API serves it (SiteFilesSetup.cs). On the server: copy the
 * folder, point an IIS website at it. No second website, no URL Rewrite, no request routing.
 *
 * The API is published with its own copy of .NET, so the server does not need the .NET 10
 * runtime. IIS still needs the ASP.NET Core Module, which is how IIS starts any .NET
 * application; it comes with the ASP.NET Core Hosting Bundle of any recent version.
 *
 * Settings. The API reads its settings from environment variables, and IIS keeps a site's
 * variables in its web.config. Any of the variables named in SETTINGS that are set when this
 * script runs are written there, so the folder arrives already configured. Their values are
 * never printed. The output folder holds secrets after that: it is git-ignored, and should be
 * moved to the server and nowhere else.
 */

import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { cpSync, existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const repositoryRoot = resolve(workspaceRoot, '..');
const SITE = resolve(workspaceRoot, 'dist/deploy');
const API_PROJECT = resolve(repositoryRoot, 'src/UPBazaar.Api');

const SIGNING_KEY = 'UPBAZAAR_Jwt__SigningKey';

/** The variables carried into web.config when they are set. */
const SETTINGS = [
  'ASPNETCORE_ENVIRONMENT',
  'UPBAZAAR_ConnectionStrings__UPBazaar',
  'UPBAZAAR_KeyVault__Uri',
  SIGNING_KEY,
];

/**
 * Host configuration for serving the site from a website of its own. Not wanted here, and a
 * web.config left inside wwwroot would be read by IIS as configuration for that folder.
 */
const NOT_SERVED = ['web.config', 'nginx.conf.example', 'README.md'];

function parseOut(argv) {
  const flag = argv.indexOf('--out');

  return resolve(workspaceRoot, flag === -1 ? '../publish/upbazaar' : argv[flag + 1]);
}

function escapeXml(value) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function unescapeXml(value) {
  return value
    .replaceAll('&quot;', '"')
    .replaceAll('&gt;', '>')
    .replaceAll('&lt;', '<')
    .replaceAll('&amp;', '&');
}

/**
 * The signing key of the package being replaced, if there is one.
 *
 * Changing the key signs everybody out, so a repackage keeps the key it had rather than making
 * a new one each time.
 */
function previousSigningKey(out) {
  const config = join(out, 'web.config');

  if (!existsSync(config)) {
    return undefined;
  }

  const match = new RegExp(`name="${SIGNING_KEY}" value="([^"]*)"`).exec(readFileSync(config, 'utf8'));

  return match ? unescapeXml(match[1]) : undefined;
}

/** Writes the settings into the aspNetCore element that `dotnet publish` generated. */
function writeSettings(out, settings) {
  const config = join(out, 'web.config');
  const xml = readFileSync(config, 'utf8');
  const variables = Object.entries(settings)
    .map(([name, value]) => `          <environmentVariable name="${name}" value="${escapeXml(value)}" />`)
    .join('\n');
  const configured = xml.replace(
    /(<aspNetCore\b[^>]*?)\s*\/>/,
    (_, element) =>
      `${element}>\n        <environmentVariables>\n${variables}\n        </environmentVariables>\n      </aspNetCore>`
  );

  if (configured === xml) {
    throw new Error(`${config} has no <aspNetCore ... /> element to add the settings to.`);
  }

  writeFileSync(config, configured);
}

function main() {
  const out = parseOut(process.argv);

  if (!existsSync(join(SITE, 'index.csr.html'))) {
    throw new Error(`No packaged site at ${SITE}. Run "node tools/scripts/package-site.mjs" first.`);
  }

  const settings = Object.fromEntries(
    SETTINGS.filter((name) => process.env[name]).map((name) => [name, process.env[name]])
  );

  settings[SIGNING_KEY] ??= previousSigningKey(out) ?? randomBytes(48).toString('base64');

  rmSync(out, { recursive: true, force: true });

  execFileSync(
    'dotnet',
    ['publish', API_PROJECT, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', out, '-v', 'q', '--nologo'],
    { stdio: 'inherit' }
  );

  cpSync(SITE, join(out, 'wwwroot'), {
    recursive: true,
    filter: (source) => !(dirname(source) === SITE && NOT_SERVED.some((name) => source.endsWith(name))),
  });

  writeSettings(out, settings);

  process.stdout.write(
    `Packaged UP Bazaar into ${out}\n` +
      '  /  /seller/  /admin/  /api   one IIS website on this folder serves all four\n' +
      `  settings written to web.config: ${Object.keys(settings).join(', ')}\n` +
      '  The folder holds secrets. Copy it to the server only.\n'
  );
}

main();
