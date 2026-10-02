# Changelog — RiftManager

## RiftManager | v1.2.0

### MAJOR UPDATE
This major release overhauls event scraping and routing from head to toe. Audited against the Hall of Legends 2026 `application.log` (120 JS paths detected, 30+ 404s) and the real Nuxt embed HTML: discovery-based CDN/catalog detection, strict EmbedWeb asset filtering, explicit per-type routing (Normal / EmbedWeb / UnityCatalog) and a presentation-layer reorganization under `Views/`.

### NEW FEATURES
   * `UrlNormalizer` (`Utils/UrlNormalizer.cs`): single URL normalization point (`{locale}`/`{bcplocale}` → `en-us`, `//` collapsing, trailing slash). Eliminates URLs like `...embed-2026//` seen in the log.
   * Explicit event routing: `EventType` (`Views/Models/EventDetails.cs`) with `RefreshType()` assigned at coordination (links merged), TFT discovery and after the catalog probe (EmbedWeb → UnityCatalog upgrade); surfaced in the technical panel through `DisplayType`.

### IMPROVEMENTS
   * Discovery-based `WebScraper`: CDN base via `__NUXT__.cdnURL` > `preload`/`script` > legacy `woff2`; `catalog.bin` by direct reference in the HTML before title guessing (`comics-pipeline-N`, `play`/`minigame` only as fallback with HEAD-check). Returns `null` for pure EmbedWeb (no Unity catalog).
   * Stricter `EmbedAssetScraper` (same root `app.*` dist scope as v1.1.x — `vendors`/`commons`/`runtime` verified asset-free on HoL 2026): JS in 3 phases (webpack `n.p+"..."` + absolute assetcdn + strictly filtered relative), relative `url()` in CSS with `data:`/`blob:` skips, `warn-and-skip` instead of throwing, per-event dedup set.
   * `DetailPageParser`: `.webp`/`.jpeg`/`.gif`/`.webm`/`.mp4`/`.ogg`/`.mp3` extensions (previously only `png`/`jpg`/`svg`; the God-King background is `.webp`).
   * `EventCoordinator`: parallel detail pages (`SemaphoreSlim` 6) and 100% lazy catalog (no eager fetch).
   * `CatalogParser`: keyword filter generalized to `comics_*`/`play_*`/`minigame_*` (previously only `comics_assets_mc_*`).
   * `BundleService`: shared injected `HttpClient` (no UA + socket exhaustion before) plus empty-bin check.
   * `RiotAudioLoader`: recursive-scan fallback (previously `Comics/` + `TopDirectoryOnly` only) and robust `CleanBaseUrl`.
   * `AssetDownloader`: single `DownloadFileCoreAsync` core (3 duplicated blocks).
   * Case-insensitive `NormalizeAssetName`; simplified `Finder` SVG regex.

### CHANGES
   * Presentation-layer reorganization: UI code grouped under `Views/` (`Views/MainWindow`, `Views/Dialogs`, `Views/Interfaces`, `Views/Models`) with matching `RiftManager.Views.*` namespaces and updated `x:Class` entries.
   * Version bumped 1.1.0.3 → 1.2.0.
   * Docs: `RiftManager.md` updated to the new layout; changelog migrated to this English `CHANGELOG.md`; legacy `changelogs.txt` removed.

### BUG FIXES
   * Fixed double slashes in embed URLs across navigation, detail and TFT parsers.
   * Fixed the HoL 404 storm (`this.svg`, `e.svg`, `window.webp`, `.svg`, `.json`, `image.jpg`, `./icon-*.svg` module-map keys) — ~20–30 s saved per event.
   * Fixed woff2-only CDN base discovery silently returning `null` for woff2-less Nuxt embeds.
   * Fixed `IsMainFile` throwing and aborting whole events, and `_downloadedAssets` leaking across events.

## RiftManager | v1.1.0.3

### HOTFIX UPDATE
No user-facing changes.

## RiftManager | v1.1.0.2

### HOTFIX UPDATE
This update focuses on improving the organization of downloaded assets for complex events.

### BUG FIXES
   * Fixed additional asset detection in `DetailPageParser` to handle Riot CDN URLs with query parameters (e.g. `?accountingTag=LoL`). The extension check now uses `Uri.LocalPath` instead of raw string matching, so `.png`, `.jpg` and `.svg` assets from detail pages are correctly discovered and downloaded.

## RiftManager | v1.1.0.1

### HOTFIX UPDATE
This update focuses on fine-tuning the new HUD interface and improving the organization of downloaded assets for complex events.

### IMPROVEMENTS
   * Hierarchical asset organization: automatic subfolder creation based on `MetagameId` (e.g. `/Assets/Event/MetagameId/`) to keep multiple comics separated.
   * Professional selection engine: redesigned link selection dialog with high-density "Studio Cards", auto-resizable horizontal layout.
   * Compact context links: single-line `- Title | MetagameId (URL)` format.
   * Banner sanitization: removed technical ID overlays from the hero banner.
   * Precise sidebar alignment (28 px) and simplified technical metadata panel.

### BUG FIXES
   * Fixed a critical read-only property binding crash on event selection.
   * Fixed XAML parsing errors in the selection dialog.
   * Interactive log now points directly at the specific comic subfolder.

## RiftManager | v1.1.0.0

### MAJOR UPDATE
UI redesign & TFT integration.

### NEW FEATURES
   * TFT event tracking: Riot Client Config API integration to discover and scrape TFT events.
   * HUD visual overhaul: full "Sidebar + Content" layout.
   * Smart versioning: centralized identity via `ApplicationInfos` from project metadata.

### IMPROVEMENTS
   * Technical metadata (types + navigation IDs) in the event details panel.
   * Redesigned hero banner, native window style for OS behavior and stability.
   * Improved embed URL resolution, including locale placeholders.

### BUG FIXES
   * Fixed XAML build errors (invalid `LetterSpacing`, panel `Padding`).
   * Fixed runtime BAML exceptions (pack URIs, resource init order).
   * Fixed image overflow on rounded banner corners.
   * Fixed double-slash URL formatting bug in regional event paths.

## RiftManager | v1.0.0.4

### HOTFIX UPDATE
This hotfix addresses critical compatibility issues with the new Riot Games asset CDNs and improves the efficiency of event processing.

### IMPROVEMENTS
   * Dynamic catalog discovery using comic number (`comics-pipeline-4`) and deploy hashes for new Riot folder structures.
   * Universal deep-search audio discovery regardless of JSON structure.
   * Automatic voice/SFX/music classification into Riot CDN folders (`AudioLocales` / `SoundFX`).
   * Lazy catalog scraping on comic selection (faster startup, fewer requests).
   * Full-event-context bundle filtering so shared thematic bundles are kept.

### BUG FIXES
   * Fixed 404s from outdated static `/Comic/` catalog paths.
   * Fixed catalog reuse across comics (now per user-selected link).
   * Fixed `BinToJson.exe` argument and case-sensitive resource path bugs.
   * Fixed shared thematic bundles being skipped.

## RiftManager | v1.0.0.3

### HOTFIX UPDATE
This hotfix focuses on improving some areas of the app and fixing some critical bugs.

### IMPROVEMENTS
   * Recursive asset URL extraction in `DetailPageParser`.
   * Asset downloads restricted to image files (no web pages).

### BUG FIXES
   * Events without a details page are now still listed.
   * Crash on `lolesports.com` assets when downloading Worlds fixed.

## RiftManager | v1.0.0.2

### HOTFIX UPDATE
Small hotfix release where I add the manifest URL to get the new assets for Riot's new game.

### NEW FEATURES
   * Added manifest URL for Riot's new game assets ("2XKO").

## RiftManager | v1.0.0.1

### HOTFIX UPDATE
Small hotfix where we released the interactive log along with some other important fixes.

### NEW FEATURES
   * Interactive log on finished event/manifest downloads.
   * Anti-spam for Riot manifest downloads.
   * New manifest assets for Riot's new game ("2XKO").
   * Missing localization switched to the general language.

### BUG FIXES
   * Fixed "List of Events" table visual error.

## RiftManager | v1.0.0.0

### MAJOR UPDATE
Initial release. I hope you like this tool and find it useful! Enjoy!

### NEW FEATURES
   * Event tracking and asset downloads for Riot Games web events.
