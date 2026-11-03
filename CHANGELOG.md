# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- A read-only text viewer for TXT, XML, META, JSON, CSV, INI, LUA, LOG and DAT files, with syntax colours, find, wrapping and zoom. CSV files open as a table. ([#46](https://github.com/Hancapo/HikariHopper/pull/46))
- Settings can turn off reopening the last game at startup, and clear the recent games. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- HikariHopper has its own logo and icon. ([#34](https://github.com/Hancapo/HikariHopper/pull/34))
- Dragging entries out of the explorer shows what is being extracted and how many. ([#23](https://github.com/Hancapo/HikariHopper/pull/23), [#25](https://github.com/Hancapo/HikariHopper/pull/25))
- Game tabs show their edition (a grey V for Legacy, green for Enhanced), and their path on hover. ([#26](https://github.com/Hancapo/HikariHopper/pull/26))
- Tabs can be reordered by dragging. ([#31](https://github.com/Hancapo/HikariHopper/pull/31), [#32](https://github.com/Hancapo/HikariHopper/pull/32))
- While a menu is open, pointing at another menu title opens that menu. ([#42](https://github.com/Hancapo/HikariHopper/pull/42))

### Changed

- Settings, About, Keyboard shortcuts and the texture editor open inside the main window instead of as separate windows. ([#35](https://github.com/Hancapo/HikariHopper/pull/35), [#36](https://github.com/Hancapo/HikariHopper/pull/36))
- The texture preview's toolbar is regrouped, and its zoom readout offers preset zooms. ([#38](https://github.com/Hancapo/HikariHopper/pull/38))
- Option groups (channels, Smooth / Pixels, Fit / 1:1, List / Grid) are sliding switches. ([#40](https://github.com/Hancapo/HikariHopper/pull/40))
- The start page is a game launcher. ([#33](https://github.com/Hancapo/HikariHopper/pull/33))
- Menus hang from their title and cast a shadow. ([#30](https://github.com/Hancapo/HikariHopper/pull/30))
- Tabs are named after the folder they are in, and Home at the root; the folder tree and the path bar no longer repeat the root. ([#43](https://github.com/Hancapo/HikariHopper/pull/43))

### Fixed

- Tab and Escape keep working in a dialog after a click on the title bar. ([#44](https://github.com/Hancapo/HikariHopper/pull/44))
- The folder tree no longer keeps the last folder outlined after going back to the root. ([#45](https://github.com/Hancapo/HikariHopper/pull/45))
- About shows the real version and credits Avalonia. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- RPFs with nothing to expand no longer show an expand arrow in the folder tree. ([#19](https://github.com/Hancapo/HikariHopper/pull/19))
- The mip slider jumps to where it is clicked. ([#20](https://github.com/Hancapo/HikariHopper/pull/20))
- Switching mips and channels in the texture viewer is instant. ([#21](https://github.com/Hancapo/HikariHopper/pull/21), [#39](https://github.com/Hancapo/HikariHopper/pull/39))
- The zoom presets open under the zoom readout. ([#41](https://github.com/Hancapo/HikariHopper/pull/41))
- Opening a texture's context menu no longer resets the preview. ([#22](https://github.com/Hancapo/HikariHopper/pull/22))
- Long folder names in the folder tree end in an ellipsis. ([#27](https://github.com/Hancapo/HikariHopper/pull/27))
- Menu titles no longer keep a white line after their menu closes, and show focus only from the keyboard. ([#29](https://github.com/Hancapo/HikariHopper/pull/29))
- Release builds no longer contain the build machine's folder paths. ([#18](https://github.com/Hancapo/HikariHopper/pull/18))

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
