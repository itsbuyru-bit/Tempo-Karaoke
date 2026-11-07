#!/usr/bin/env node
// Snapshot, list and restore versions of the static site in `site/`.
//
// Usage:
//   node scripts/site-version.mjs list
//   node scripts/site-version.mjs snapshot [name]
//   node scripts/site-version.mjs restore <name>
//
// Archives live outside `site/`, so GitHub Pages (which uploads `site/`) never
// publishes them. Restoring first copies the current site to
// `site-versions/_backup-<timestamp>` so an accidental restore is undoable.

import {
  cpSync,
  existsSync,
  mkdirSync,
  readdirSync,
  rmSync,
  statSync,
} from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const siteDir = path.join(root, 'site');
const versionsDir = path.join(root, 'site-versions');

function usage() {
  console.log(`Usage:
  node scripts/site-version.mjs list
  node scripts/site-version.mjs snapshot [name]
  node scripts/site-version.mjs restore <name>`);
}

function versionNames() {
  if (!existsSync(versionsDir)) return [];
  return readdirSync(versionsDir, { withFileTypes: true })
    .filter(entry => entry.isDirectory() && !entry.name.startsWith('_'))
    .map(entry => entry.name)
    .sort();
}

function timestamp() {
  return new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
}

function list() {
  const names = versionNames();
  if (!names.length) {
    console.log('No site versions yet. Run: node scripts/site-version.mjs snapshot <name>');
    return;
  }
  for (const name of names) {
    const target = path.join(versionsDir, name, 'index.html');
    const when = existsSync(target) ? statSync(target).mtime.toISOString() : 'unknown';
    console.log(`${name}\t${when}`);
  }
}

function snapshot(name) {
  if (!existsSync(path.join(siteDir, 'index.html'))) {
    throw new Error('site/index.html not found');
  }
  const label = name || `snapshot-${timestamp()}`;
  if (label.startsWith('_') || label.includes('/') || label.includes('\\')) {
    throw new Error('Version name must be a simple directory name and cannot start with "_".');
  }
  const target = path.join(versionsDir, label);
  if (existsSync(target)) throw new Error(`Version already exists: ${label}`);
  mkdirSync(versionsDir, { recursive: true });
  cpSync(siteDir, target, { recursive: true });
  console.log(`Saved site/ -> site-versions/${label}`);
}

function restore(name) {
  if (!name) throw new Error('restore requires a version name');
  const source = path.join(versionsDir, name);
  if (!existsSync(path.join(source, 'index.html'))) {
    throw new Error(`Version not found: ${name}`);
  }
  mkdirSync(versionsDir, { recursive: true });
  const backup = path.join(versionsDir, `_backup-${timestamp()}`);
  cpSync(siteDir, backup, { recursive: true });
  rmSync(siteDir, { recursive: true, force: true });
  cpSync(source, siteDir, { recursive: true });
  console.log(`Restored site-versions/${name} -> site/`);
  console.log(`Previous site saved in site-versions/${path.basename(backup)}`);
}

const [command, name] = process.argv.slice(2);
try {
  if (command === 'list') list();
  else if (command === 'snapshot') snapshot(name);
  else if (command === 'restore') restore(name);
  else {
    usage();
    process.exitCode = 1;
  }
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
