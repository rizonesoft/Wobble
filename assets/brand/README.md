# Brand assets

The Wobble mark is the pregroove of a recordable disc: a ring that wobbles so the drive can find its place, drawn on a dark tile in the violet, cyan and pink a disc reflects. The owner chose it on 2026-11-15 (direction "Groove") over two alternatives. The interface that goes with it is specified in [`design/SPEC.md`](../../design/SPEC.md).

| File | Use |
| ---- | --- |
| `wobble-icon.svg` | The icon. The source for every size above 32 pixels. |
| `wobble-icon-small.svg` | A heavier drawing with one ring, for 32 pixels and below. |
| `wobble-icon-512.png`, `wobble-icon-192.png` | Renders of the icon. |
| `wobble.ico` | Windows icon with 16 to 256 pixel frames; the app's icon and the base of the campaign launchers' icons. |
| `social-preview.svg`, `social-preview.png` | The 1280 by 640 image for the repository's social preview. |
| `../icons/tray-*.svg` | The tray icon with its state badge: idle, reading, writing, fault. |
| `../icons/media-*.svg` | Media glyphs: empty tray, CD, DVD and Blu-ray, write-once and rewritable. |

## Colours

| Name | Value | Where |
| ---- | ----- | ----- |
| Tile, top | `#1b1830` | Background gradient start |
| Tile, bottom | `#09080f` | Background gradient end |
| Tile edge | `#2c2747` | The inner border of the tile |
| Violet | `#a78bfa` | Spectrum start |
| Cyan | `#38d6ee` | Spectrum middle |
| Pink | `#f472b6` | Spectrum end |
| Hub | `#e9e6ff` | The hub ring |

## Rendering

The SVG files are the source. Each icon was drawn once at 1024 pixels in headless Chrome with a transparent background; the 512 and 192 pixel PNG files are resized from that render, and `wobble.ico` was made by `python scripts/campaign-icon.py frames <large.png> <small.png> assets/brand/wobble.ico`, which takes the small drawing up to 64 pixels. `social-preview.png` is `social-preview.svg` drawn in headless Chrome at 1280 by 640 with Sora and IBM Plex Sans loaded. The renders are committed so nothing in the repository needs an image library. Render them again only when an SVG changes.

## Launcher icons

`pwsh scripts/start-campaign.ps1 -InstallShortcut` writes "Wobble Claude campaign" and "Wobble Carbon campaign" to the desktop. Their icons are the mark with the runner's badge, rendered on the machine into `%LOCALAPPDATA%\Wobble` by `scripts/campaign-icon.py badge` and never committed.
