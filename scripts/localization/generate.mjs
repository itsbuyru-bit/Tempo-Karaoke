#!/usr/bin/env node
// Generates the platform artifacts from the single localization source.
//
//   localization/manifest.json      ← the only supported-language list
//   localization/<tag>.json         ← flat key/value catalogs
//        ↓
//   src/TempoKaraoke/Strings/<tag>/Resources.resw   (desktop / MRT)
//   src/TempoKaraoke/public/locales/<tag>.json      (web remote)
//   src/TempoKaraoke/public/locale-manifest.json    (web language selector)
//
// Usage:
//   node scripts/localization/generate.mjs          write artifacts
//   node scripts/localization/generate.mjs --check  fail when artifacts are stale
//
// Key routing rule: a key is a web key when it has no property suffix ('.')
// and no uppercase letters (tab_queue, playlist_tracks_one). Everything else is
// a desktop resource key (Navigation_Home.Content, Plural_SongCount_One).

import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const sourceDir = path.join(root, 'localization');
const appDir = path.join(root, 'src', 'TempoKaraoke');
const publicDir = path.join(appDir, 'public');
const check = process.argv.includes('--check');

const manifestPath = path.join(sourceDir, 'manifest.json');
if (!fs.existsSync(manifestPath)) {
  console.error(`Manifest not found: ${manifestPath}`);
  process.exit(2);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
if (!manifest || !Array.isArray(manifest.languages)) {
  console.error('manifest.json must contain a "languages" array.');
  process.exit(2);
}

const isWebKey = key => !key.includes('.') && key === key.toLowerCase();
const sortKeys = value => Object.fromEntries(
  Object.keys(value).sort().map(key => [key, value[key]])
);
const xmlEscape = value => value
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;');

const RESW_HEADER = `<?xml version='1.0' encoding='utf-8'?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
`;

function buildResw(catalog) {
  const lines = Object.keys(catalog)
    .filter(key => !isWebKey(key))
    .sort()
    .map(key => `  <data name="${key}" xml:space="preserve"><value>${xmlEscape(catalog[key])}</value></data>`);
  return RESW_HEADER + lines.join('\n') + '\n</root>\n';
}

function buildLocaleJson(catalog) {
  const web = sortKeys(Object.fromEntries(
    Object.entries(catalog).filter(([key]) => isWebKey(key))
  ));
  return JSON.stringify(web, null, 2) + '\n';
}

const outputs = new Map();

const webManifest = {
  version: manifest.version ?? 1,
  fallback: manifest.fallback,
  languages: [],
};

for (const language of manifest.languages) {
  if (!language || language.enabled === false) {
    continue;
  }

  const catalogPath = path.join(sourceDir, language.file);
  if (!fs.existsSync(catalogPath)) {
    console.error(`Catalog not found for ${language.tag}: ${catalogPath}`);
    process.exit(2);
  }

  const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
  outputs.set(path.join(appDir, 'Strings', language.tag, 'Resources.resw'), buildResw(catalog));
  outputs.set(path.join(publicDir, 'locales', `${language.tag}.json`), buildLocaleJson(catalog));
  webManifest.languages.push({
    tag: language.tag,
    nativeName: language.nativeName,
    englishName: language.englishName ?? '',
  });
}

outputs.set(
  path.join(publicDir, 'locale-manifest.json'),
  JSON.stringify(webManifest, null, 2) + '\n'
);

let stale = 0;
for (const [file, content] of outputs) {
  const current = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null;
  if (current === content) {
    continue;
  }

  stale++;
  if (check) {
    console.error(`stale: ${path.relative(root, file)}`);
    continue;
  }

  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content, 'utf8');
  console.log(`wrote ${path.relative(root, file)}`);
}

if (check) {
  if (stale > 0) {
    console.error(`\n${stale} generated file(s) are out of date. Run: node scripts/localization/generate.mjs`);
    process.exit(1);
  }

  console.log('Localization artifacts are up to date.');
} else if (stale === 0) {
  console.log('Localization artifacts are already up to date.');
}
