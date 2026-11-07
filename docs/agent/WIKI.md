# GitHub Wiki generation

The repository `docs/` folder is the only source of truth for the Wiki. Do not
edit wiki pages by hand: change `docs/` and regenerate.

## Local preview

```powershell
node scripts/wiki/generate.mjs --out artifacts/wiki
```

The generator creates `Home`, `_Sidebar`, `_Footer` and one page per Markdown
file under `docs/`. English and Russian guides use paired page names
(`en-features` / `ru-features`); release notes and agent notes are unpaired.

Page names are stable:

* `docs/en/<name>.md` → `en-<slug>`
* `docs/ru/<name>.md` → `ru-<slug>`
* `docs/releases/<tag>.md` → `release-<slug>`
* `docs/agent/<name>.md` → `agent-<slug>`
* `docs/README.md` → `docs-index`

Relative links between generated pages are rewritten to wiki links. Links to
repository files outside `docs/`, such as `PRIVACY.md` or `CHANGELOG.md`, point
to GitHub.

## Publishing

1. Create the first Wiki page once in the GitHub UI. This initializes the
   separate `Tempo-Karaoke.wiki.git` repository.
2. Add a repository secret `WIKI_TOKEN` with `repo` access.
3. Push changes to `docs/`, `scripts/wiki/` or the workflow. The `wiki`
   workflow regenerates and pushes the wiki.

The workflow skips publication with a notice if `WIKI_TOKEN` is not configured or
if the wiki repository has not been initialized yet; it never publishes stale
content silently.
