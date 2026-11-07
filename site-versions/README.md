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
| `2026-11-07-live-remote` | Dark bento landing, real `RemotePage` clone in an iframe, mock search/queue/playback, and app screenshots in a grid. |
| `2026-11-07-carousel` | Current landing: live remote clone with a dark themed scrollbar, richer layered background, and app screenshots as a swipeable film strip. |
| `2026-11-07-caption-carousel` | Current landing: Bootstrap-style screenshot carousel with overlaid captions, indicators and prev/next controls. |
| `2026-11-07-bootstrap-carousel` | Current landing: app screenshots recaptured at two-thirds height, served through the vendored Bootstrap carousel data API. |
| `2026-11-07-aurora` | Current landing: brighter aurora background (blue, violet, pink, teal and warm glows) behind the Bootstrap carousel. |
| `2026-11-07-playlists` | Current landing: new app icon, blurred icon glow, Bootstrap chevrons, and a five-slide carousel with the English playlists screen. |
| `2026-11-07-showcase` | Current landing: tabbed screenshot showcase with window frame, autoplay, keyboard navigation, fullscreen lightbox and the cropped app captures. |

Archives are committed so a previous look can be restored without relying on
the git history of the `site/` directory. To try one without touching the
working tree, serve it directly, for example:

```bash
npx serve site-versions/2026-11-07-bento
```
