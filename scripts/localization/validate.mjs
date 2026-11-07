#!/usr/bin/env node
// Fast validation for a localization pull request. Runs without the .NET SDK.
//
// Usage: node scripts/localization/validate.mjs

import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const sourceDir = path.join(root, 'localization');
const errors = [];
const warnings = [];

const fail = message => errors.push(message);
const warn = message => warnings.push(message);
const readJson = (file, label) => {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    fail(`${label}: invalid JSON (${error.message})`);
    return null;
  }
};

const manifestPath = path.join(sourceDir, 'manifest.json');
let manifest = null;
if (!fs.existsSync(manifestPath)) {
  fail('localization/manifest.json is missing');
} else {
  manifest = readJson(manifestPath, 'localization/manifest.json');
  if (manifest) {
    if (!Array.isArray(manifest.languages) || manifest.languages.length === 0) {
      fail('manifest.json: "languages" must be a non-empty array');
    }
    if (typeof manifest.fallback !== 'string' || !manifest.fallback.trim()) {
      fail('manifest.json: "fallback" is required');
    }

    const seen = new Set();
    const enabled = [];
    for (const [index, language] of (manifest.languages ?? []).entries()) {
      const at = `manifest.json languages[${index}]`;
      if (!language || typeof language.tag !== 'string' || !language.tag.trim()) {
        fail(`${at}: "tag" is required`);
        continue;
      }
      const tag = language.tag.trim();
      try {
        // Validates BCP 47 tags such as en-US, ru-RU, zh-Hans-CN.
        new Intl.Locale(tag);
      } catch {
        fail(`${at}: "${tag}" is not a valid BCP 47 tag`);
      }
      if (seen.has(tag.toLowerCase())) fail(`${at}: duplicate language tag "${tag}"`);
      seen.add(tag.toLowerCase());
      if (typeof language.nativeName !== 'string' || !language.nativeName.trim()) {
        fail(`${at}: "nativeName" is required`);
      }
      if (typeof language.file !== 'string' || !language.file.trim()) {
        fail(`${at}: "file" is required`);
      } else {
        const catalogPath = path.resolve(sourceDir, language.file);
        if (!catalogPath.startsWith(sourceDir + path.sep) || !fs.existsSync(catalogPath)) {
          fail(`${at}: catalog "${language.file}" is missing or outside localization/`);
        }
      }
      if (language.enabled !== false) enabled.push(tag);
    }

    if (manifest.fallback && !seen.has(String(manifest.fallback).toLowerCase())) {
      fail(`manifest.json: fallback "${manifest.fallback}" is not listed`);
    }
    if (manifest.fallback && !enabled.some(tag => tag.toLowerCase() === String(manifest.fallback).toLowerCase())) {
      fail(`manifest.json: fallback "${manifest.fallback}" must be enabled`);
    }
  }
}

const enPath = path.join(sourceDir, 'en-US.json');
const english = fs.existsSync(enPath) ? readJson(enPath, 'localization/en-US.json') : null;
if (!english) {
  if (!fs.existsSync(enPath)) fail('localization/en-US.json is missing');
} else {
  for (const [key, value] of Object.entries(english)) {
    if (!key.trim()) fail('en-US.json: empty key');
    if (typeof value !== 'string' || !value.trim()) fail(`en-US.json: "${key}" must be a non-empty string`);
    if (/[А-Яа-яЁё]/.test(value)) fail(`en-US.json: "${key}" contains Cyrillic text`);
  }
}

const placeholders = value => [...String(value).matchAll(/\{([^{}]+)\}/g)]
  .map(match => match[1].trim())
  .sort();
const englishKeys = new Set(Object.keys(english ?? {}));

if (manifest && Array.isArray(manifest.languages)) {
  for (const language of manifest.languages) {
    if (!language || language.enabled === false || typeof language.file !== 'string') continue;
    const file = path.resolve(sourceDir, language.file);
    if (!file.startsWith(sourceDir + path.sep) || !fs.existsSync(file)) continue;
    const catalog = readJson(file, `localization/${language.file}`);
    if (!catalog) continue;

    for (const [key, value] of Object.entries(catalog)) {
      if (typeof value !== 'string' || !value.trim()) {
        fail(`${language.file}: "${key}" must be a non-empty string`);
        continue;
      }
      if (english && !englishKeys.has(key)) {
        warn(`${language.file}: "${key}" is not present in en-US.json`);
        continue;
      }
      if (english && englishKeys.has(key)) {
        const expected = JSON.stringify(placeholders(english[key]));
        const actual = JSON.stringify(placeholders(value));
        if (expected !== actual) {
          fail(`${language.file}: placeholders for "${key}" differ from en-US.json`);
        }
      }
    }

    for (const key of englishKeys) {
      if (!(key in catalog)) warn(`${language.file}: "${key}" is missing; English fallback will be used`);
    }
  }
}

for (const message of warnings) console.warn(`warning: ${message}`);
if (errors.length) {
  for (const message of errors) console.error(`error: ${message}`);
  console.error(`\nLocalization validation failed with ${errors.length} error(s).`);
  process.exit(1);
}
console.log('Localization catalogs are valid.');
