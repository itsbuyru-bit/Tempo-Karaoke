# Site versions

`site/` is the only directory deployed to GitHub Pages. Archived versions live
here and are never uploaded by the `pages` workflow.

## Commands

```bash
node scripts/site-version.mjs list
node scripts/site-version.mjs snapshot 2026-11-07-live-remote
node scripts/site-version.mjs restore 2026-11-07-bento
```

`restore` copies the current `site/` to `site-versions/_backup-<timestamp>`
before replacing it, so a restore can be undone the same way.

## Current archive

| Version | What it is |
| --- | --- |
| `2026-11-07-bento` | The dark bento landing with the hand-built remote preview (the version deployed before the real-remote clone). |
| `2026-11-07-live-remote` | The current landing: dark bento layout, real `RemotePage` clone in an iframe, mock search/queue/playback, and app screenshots. |

Archives are committed so a previous look can be restored without relying on
the git history of the `site/` directory. To try one without touching the
working tree, serve it directly, for example:

```bash
npx serve site-versions/2026-11-07-bento
```
