# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Settings can turn off reopening the last game at startup, and clear the recent games the start page lists. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- HikariHopper has its own logo and icon: the wordmark with a crescent moon for the dot of the i, and a rabbit leaping across the moon. They appear on the exe, the taskbar, the title bars, the About window and the splash screen. ([#34](https://github.com/Hancapo/HikariHopper/pull/34))
- Dragging entries out of the explorer shows a cursor with an icon for what is being extracted and the number of items. ([#23](https://github.com/Hancapo/HikariHopper/pull/23), [#25](https://github.com/Hancapo/HikariHopper/pull/25))
- A game tab shows which edition it has open with a V, grey for Legacy and green for Enhanced, and hovering a tab shows its path. ([#26](https://github.com/Hancapo/HikariHopper/pull/26))
- Tabs can be dragged along the tab strip to reorder them, and when a tab closes the ones after it glide into place. ([#31](https://github.com/Hancapo/HikariHopper/pull/31), [#32](https://github.com/Hancapo/HikariHopper/pull/32))
- While a menu is open, pointing at another title in the menu bar opens that menu instead, in the explorer and in the texture editor. ([#42](https://github.com/Hancapo/HikariHopper/pull/42))

### Changed

- Settings, About, Keyboard shortcuts and the texture editor open inside the main window, over the blurred explorer, instead of as separate windows, and size themselves with it. Settings lists each game's installations with their edition marks and a clear status, and the empty future-game rows are gone. ([#35](https://github.com/Hancapo/HikariHopper/pull/35), [#36](https://github.com/Hancapo/HikariHopper/pull/36))
- The texture preview's toolbar groups its controls: the channels, with each letter in its own colour; the transparency grid toggle; a Smooth / Pixels switch; and the zoom, whose readout opens preset zooms from 25% to 800%. ([#38](https://github.com/Hancapo/HikariHopper/pull/38))
- The channel, Smooth / Pixels, Fit / 1:1 and List / Grid controls are switches: one well whose raised segment slides to the option in use, rounded only at the well's ends and straight where it meets a neighbour. ([#40](https://github.com/Hancapo/HikariHopper/pull/40))
- The start page is a launcher: the games to open, grouped by game with their edition marks, and a standalone RPF. It no longer shows an empty explorer around them, nor the tab strip while it is the only tab. ([#33](https://github.com/Hancapo/HikariHopper/pull/33))
- Menus hang from the title that opened them as one surface, and every menu casts a soft shadow. ([#30](https://github.com/Hancapo/HikariHopper/pull/30))
- A tab is named after the folder or RPF it is in, like a file manager's tab: Home at a game's root, and the RPF's name at the root of an RPF opened on its own. The folder tree starts with what is inside the root instead of a row for the root. At the root the path bar is empty, and inside a folder it starts with a home mark that goes back to the root. ([#43](https://github.com/Hancapo/HikariHopper/pull/43))

### Fixed

- With Settings or another dialog open, Tab and Escape keep working after a click on the title bar: Tab goes back into the dialog and Escape closes it. ([#44](https://github.com/Hancapo/HikariHopper/pull/44))
- About shows the app's real version, and credits Avalonia instead of Qt. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- The folder tree shows an expand arrow only on RPFs that have folders or other RPFs inside. ([#19](https://github.com/Hancapo/HikariHopper/pull/19))
- The mip slider moves to wherever it is pressed, not only when its handle is dragged. ([#20](https://github.com/Hancapo/HikariHopper/pull/20))
- Moving between mips in the texture viewer is instant. ([#21](https://github.com/Hancapo/HikariHopper/pull/21))
- The texture preview's zoom presets open centred under the zoom readout, even when the window reaches the edge of the screen, and list only zooms: fitting has its own button. ([#41](https://github.com/Hancapo/HikariHopper/pull/41))
- Switching between RGBA and the R, G, B and A channels in the texture viewer is instant, and the preview no longer goes blank while a channel is made. ([#39](https://github.com/Hancapo/HikariHopper/pull/39))
- Opening a texture's context menu no longer makes the preview blink or jump back to the first mip. ([#22](https://github.com/Hancapo/HikariHopper/pull/22))
- Long folder names in the folder tree are cut off with an ellipsis inside their row instead of running past it. ([#27](https://github.com/Hancapo/HikariHopper/pull/27))
- A menu title no longer keeps a white line under it after its menu closes. Menu titles and window buttons now show focus only when it comes from the keyboard. ([#29](https://github.com/Hancapo/HikariHopper/pull/29))
- Release builds no longer contain the folder paths of the machine that built them. ([#18](https://github.com/Hancapo/HikariHopper/pull/18))

### Removed

## [0.0.2] - 2026-11-02

### Changed

- Text uses Avalonia's own rendering, keeping each font's letter widths and line heights instead of rounding them the way Qt does. ([#13](https://github.com/Hancapo/HikariHopper/pull/13))
- Releases are native builds and no longer need the .NET 10 Runtime. ([#14](https://github.com/Hancapo/HikariHopper/pull/14))

## [0.0.1] - 2026-11-02

### Added

- RPF explorer for GTA V Legacy and Enhanced, with nested archives, a folder tree, list and grid views, search and tabs.
- Texture editor for YTD dictionaries and textures embedded in YDR drawables.

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...HEAD
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
