# Changelog

## 1.4.0 — 2026-10-11

- Full English app interface alongside German: all pages, tray controls and menus, dialogs, recovery prompts, shortcut labels, statuses, and app-generated errors.
- Persistent language selection in Settings and the tray menu, applied immediately without restarting or re-querying monitor hardware.
- Language changes preserve pending live slider targets, confirmed monitor values, custom aliases, profiles, and key bindings.
- Localized installer and uninstaller prompts follow the saved app language; Windows-owned dialogs keep the Windows language.
- 54 self-tests and 23 WPF/tray integration checks, including language switching during live adjustments and untranslated user names.
- English screenshots and updated bilingual documentation.

## 1.3.0 — 2026-10-11

- Live brightness, contrast, RGB, white point, and dimming in app and tray; removed manual brightness/color Apply buttons.
- Latest-value queues per monitor/control with shared-adapter serialization. Capture, workspace changes, and exit wait for pending writes.
- Update controls in place from readback; update dimming opacity without recreating the overlay.
- Remove redundant fixed delays after native setters; retain bounded stale-readback retries.
- Fit the window to small scaled work areas, stack narrow-page cards, simplify narrow rows, and constrain the tray panel to its work area.
- Recover incomplete settings, preserve custom shortcuts, and back up malformed startup files. Validate profiles before display changes.
- Fix guardian launch through the dotnet host during development.
- 47 self-tests and 14 WPF integration checks, anonymized images, compatibility/performance documentation, release packaging, and Windows CI.

## 1.2.0 — 2026-10-11

- Reuse capabilities and display modes while reading current values afresh.
- Discover brightness first, enrich controls in the background, and parallelize independent adapters.
- Uncompressed self-contained executable to reduce startup decompression and private memory copies.

## 1.1.x — 2026-10-11

- Light/dark/system themes, themed dropdowns, tray quick control and expanded menu.
- Targeted readbacks after control adjustments.

## 1.0.0 — 2026-10-11

- Initial monitor detection, focus/restore, profiles, open-window placement, DDC controls, dimming, patterns, hotkeys, schedules, and per-user installer.
