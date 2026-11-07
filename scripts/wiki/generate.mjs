#!/usr/bin/env node
/**
 * Generates a GitHub Wiki tree from the repository's docs/ folder.
 *
 * The documentation remains the only source of truth. This script maps every
 * Markdown file to a stable wiki page name, rewrites relative links to wiki
 * links (or GitHub URLs for files outside docs/) and builds Home, Sidebar and
 * Footer pages.
 *
 * Usage:
 *   node scripts/wiki/generate.mjs
 *   node scripts/wiki/generate.mjs --out build/wiki
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const docsDir = path.join(repoRoot, 'docs');
const repoUrl = 'https://github.com/itsbuyru-bit/Tempo-Karaoke';
const rawUrl = 'https://raw.githubusercontent.com/itsbuyru-bit/Tempo-Karaoke/main';

const outIndex = process.argv.indexOf('--out');
const outDir = path.resolve(
  repoRoot,
  outIndex >= 0 && process.argv[outIndex + 1] ? process.argv[outIndex + 1] : 'artifacts/wiki',
);

const walk = (dir) =>
  fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });

const slug = (value) =>
  value
    .toLowerCase()
    .replace(/\.md$/i, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');

function pageName(file) {
  const rel = path.relative(docsDir, file).split(path.sep).join('/');
  if (rel === 'README.md') return 'docs-index';
  const parts = rel.replace(/\.md$/i, '').split('/');
  if (parts.length === 1) return slug(parts[0]);
  if (parts[0] === 'en' || parts[0] === 'ru') {
    return `${parts[0]}-${slug(parts.slice(1).join('-'))}`;
  }
  if (parts[0] === 'releases') return `release-${slug(parts.slice(1).join('-'))}`;
  if (parts[0] === 'agent') return `agent-${slug(parts.slice(1).join('-'))}`;
  return slug(parts.join('-'));
}

function titleOf(markdown, fallback) {
  const match = markdown.match(/^#\s+(.+)$/m);
  if (match) return match[1].replace(/[*_`]/g, '').trim();
  if (fallback.startsWith('release-')) {
    return `Tempo Karaoke ${fallback.replace(/^release-/, '').replace(/-/g, '.')}`;
  }
  return fallback;
}

const files = walk(docsDir).filter((file) => file.toLowerCase().endsWith('.md')).sort();
const byFile = new Map(files.map((file) => [path.resolve(file), pageName(file)]));
const byPage = new Map();
for (const file of files) {
  const markdown = fs.readFileSync(file, 'utf8');
  byPage.set(byFile.get(path.resolve(file)), {
    file,
    rel: path.relative(docsDir, file).split(path.sep).join('/'),
    markdown,
    title: titleOf(markdown, pageName(file)),
    page: byFile.get(path.resolve(file)),
  });
}

function repoLink(relativePath, isDirectory = false) {
  const clean = relativePath.replace(/\\/g, '/').replace(/^docs\//, 'docs/');
  return `${repoUrl}/${isDirectory ? 'tree' : 'blob'}/main/${clean}`;
}

function rewriteTarget(target, sourceFile, image) {
  if (!target || target.startsWith('#') || /^(https?:|mailto:|data:)/i.test(target)) {
    return target;
  }

  const [rawPath, anchor] = target.split('#', 2);
  if (!rawPath) return target;

  let decoded = rawPath;
  try {
    decoded = decodeURI(rawPath);
  } catch {
    // Keep the original path if it contains invalid percent escapes.
  }

  if (/\.(png|jpe?g|gif|svg|webp)$/i.test(decoded)) {
    const absolute = path.resolve(path.dirname(sourceFile), decoded);
    const rel = path.relative(repoRoot, absolute).split(path.sep).join('/');
    return `${rawUrl}/${rel}${anchor ? `#${anchor}` : ''}`;
  }

  if (!decoded.endsWith('.md') && !decoded.endsWith('/')) {
    const absolute = path.resolve(path.dirname(sourceFile), decoded);
    if (!byFile.has(absolute)) return target;
  }

  const absolute = path.resolve(path.dirname(sourceFile), decoded);
  const mapped = byFile.get(absolute);
  if (mapped) return `${mapped}${anchor ? `#${anchor}` : ''}`;

  if (decoded.endsWith('/')) {
    const rel = path.relative(repoRoot, absolute).split(path.sep).join('/');
    return `${repoLink(rel, true)}${anchor ? `#${anchor}` : ''}`;
  }

  const rel = path.relative(repoRoot, absolute).split(path.sep).join('/');
  if (!rel.startsWith('..')) return `${repoLink(rel)}${anchor ? `#${anchor}` : ''}`;
  return target;
}

function rewriteMarkdown(markdown, sourceFile) {
  let inFence = false;
  return markdown
    .split(/\r?\n/)
    .map((line) => {
      if (/^\s*```/.test(line)) {
        inFence = !inFence;
        return line;
      }
      if (inFence) return line;

      let result = line.replace(
        /!\[([^\]]*)\]\(([^)\s]+)(\s+"[^"]*")?\)/g,
        (_all, alt, target, title = '') =>
          `![${alt}](${rewriteTarget(target, sourceFile, true)}${title})`,
      );
      result = result.replace(
        /(?<!!)\[([^\]]+)\]\(([^)\s]+)(\s+"[^"]*")?\)/g,
        (_all, label, target, title = '') =>
          `[${label}](${rewriteTarget(target, sourceFile, false)}${title})`,
      );
      return result;
    })
    .join('\n');
}

const mainPages = files.filter((file) => {
  const page = byFile.get(path.resolve(file));
  return !page.startsWith('release-') && !page.startsWith('agent-') && page !== 'docs-index';
});
const releasePages = files.filter((file) => byFile.get(path.resolve(file)).startsWith('release-'));
const agentPages = files.filter((file) => byFile.get(path.resolve(file)).startsWith('agent-'));

function groupPages(predicate) {
  return [...byPage.values()]
    .filter(predicate)
    .sort((a, b) => a.title.localeCompare(b.title, 'en'));
}

function linksFor(pages) {
  return pages.map((page) => `- [${page.title}](${page.page})`).join('\n');
}

const enGuidePages = groupPages((page) => page.page.startsWith('en-'));
const ruGuidePages = groupPages((page) => page.page.startsWith('ru-'));
const releasePagesSorted = [...releasePages]
  .map((file) => byPage.get(byFile.get(path.resolve(file))))
  .filter(Boolean)
  .sort((a, b) => b.page.localeCompare(a.page, 'en'));
const agentPagesSorted = groupPages((page) => page.page.startsWith('agent-'));

const home = `# Tempo Karaoke Wiki

**English** · **[Русский](#русский)**

A native Windows karaoke player for a local video library: smart search, a
persistent queue, playlists, a phone remote and safe updates.

## English guides

${linksFor(enGuidePages)}

- [Documentation index](docs-index)

## Русский

Нативный караоке-плеер для Windows с локальной медиатекой, умным поиском,
очередью, плейлистами, веб-пультом и безопасными обновлениями.

${linksFor(ruGuidePages)}

## Release notes / Заметки к релизам

${releasePagesSorted.map((page) => `- [${page.title}](${page.page})`).join('\n')}

## Agent notes / Заметки для агентов

${agentPagesSorted.map((page) => `- [${page.title}](${page.page})`).join('\n')}

## Repository

- [Source code](${repoUrl})
- [Releases](${repoUrl}/releases)
- [Website](https://itsbuyru-bit.github.io/Tempo-Karaoke/)
`;

const sidebar = `**[Home](Home) · [Website](https://itsbuyru-bit.github.io/Tempo-Karaoke/)**

### English

${linksFor(enGuidePages)}

### Русский

${linksFor(ruGuidePages)}

### Release notes

${releasePagesSorted.map((page) => `- [${page.title}](${page.page})`).join('\n')}

### Agent notes

${agentPagesSorted.map((page) => `- [${page.title}](${page.page})`).join('\n')}
`;

const footer = `Tempo Karaoke · [GitHub](${repoUrl}) · [Releases](${repoUrl}/releases) · [MIT license](${repoUrl}/blob/main/LICENSE)`;

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(path.join(outDir, 'Home.md'), home, 'utf8');
fs.writeFileSync(path.join(outDir, '_Sidebar.md'), sidebar, 'utf8');
fs.writeFileSync(path.join(outDir, '_Footer.md'), footer, 'utf8');

for (const page of byPage.values()) {
  const prefix = page.page.startsWith('release-') || page.page.startsWith('agent-')
    ? '[← Home](Home)\n\n'
    : '';
  fs.writeFileSync(
    path.join(outDir, `${page.page}.md`),
    `${prefix}${rewriteMarkdown(page.markdown, page.file)}\n`,
    'utf8',
  );
}

const pageCount = byPage.size + 3;
console.log(`Generated ${pageCount} wiki pages in ${path.relative(repoRoot, outDir)}`);
