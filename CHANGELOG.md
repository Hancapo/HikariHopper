# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- HikariHopper updates itself: new versions are offered with what changed in them, and install with a restart. ([#87](https://github.com/Hancapo/HikariHopper/pull/87))
- The update check at startup can be turned off in Settings. ([#87](https://github.com/Hancapo/HikariHopper/pull/87))
- An Updates page in Settings, to check for updates. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))

### Changed

- The in-app changelog now loads from GitHub, so it stays up to date between releases. ([#86](https://github.com/Hancapo/HikariHopper/pull/86))
- The release zip updates itself, so it needs a folder you can write to. ([#87](https://github.com/Hancapo/HikariHopper/pull/87))
- Settings has a page per group, picked from a rail with icons. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))
- Settings that are on or off use Off/On switches. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))
- The panels are taller, and the status bar is centred under them. ([#89](https://github.com/Hancapo/HikariHopper/pull/89))

### Fixed

### Removed

- Typing a game folder in Settings; choose it with the folder picker instead. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))

## [0.0.4] - 2026-11-06

### Added

- Import images as new textures in the texture editor. ([#57](https://github.com/Hancapo/HikariHopper/pull/57))
- Image viewer for DDS, PNG, JPEG, BMP, TGA and WebP files, with channels, zoom and DDS mips. ([#62](https://github.com/Hancapo/HikariHopper/pull/62))
- Extract files and folders to a folder of your choice, or an RPF's whole content at once. ([#68](https://github.com/Hancapo/HikariHopper/pull/68))
- Copy and paste files and folders, in and out of RPFs and to and from Windows Explorer. ([#69](https://github.com/Hancapo/HikariHopper/pull/69))
- Rename files and folders, on disk or inside an RPF. ([#70](https://github.com/Hancapo/HikariHopper/pull/70))
- Properties for files and folders: size and location, a texture dictionary's contents, and an RPF's encryption and file count. ([#71](https://github.com/Hancapo/HikariHopper/pull/71))
- Editable address bar: type or paste a path to go there, or copy the current one. ([#75](https://github.com/Hancapo/HikariHopper/pull/75))
- Search the current folder, the current RPF or the whole game, nested RPFs included, with plain text, wildcards or regular expressions. ([#77](https://github.com/Hancapo/HikariHopper/pull/77))

### Changed

- Removed the window's blue border. ([#55](https://github.com/Hancapo/HikariHopper/pull/55))
- Regrouped the texture context menu and added icons. ([#56](https://github.com/Hancapo/HikariHopper/pull/56))
- Texture editor and text viewer headers now show the file's icon and name. ([#63](https://github.com/Hancapo/HikariHopper/pull/63))
- Image and text files get their own icons and type names, such as PNG Image and DDS Texture. ([#63](https://github.com/Hancapo/HikariHopper/pull/63))
- The in-app changelog only lists released versions; test builds also show unreleased changes. ([#64](https://github.com/Hancapo/HikariHopper/pull/64))
- Files inside an archive show their size in the archive, and compressed files are labeled as such. ([#66](https://github.com/Hancapo/HikariHopper/pull/66))
- The context menu now matches what you right-click. ([#67](https://github.com/Hancapo/HikariHopper/pull/67))
- Ctrl+C now copies the selected files, and you can drop folders onto the explorer. ([#69](https://github.com/Hancapo/HikariHopper/pull/69))
- The title bar and splash screen now say "Modding Toolkit". ([#72](https://github.com/Hancapo/HikariHopper/pull/72))
- New app icon: a railway conductor's cap on a brass tile. ([#73](https://github.com/Hancapo/HikariHopper/pull/73))
- Dialogs and viewers now close smoothly, shrinking as they fade out. ([#78](https://github.com/Hancapo/HikariHopper/pull/78))
- Removed the "ICON FIELD" title from the grid header. ([#79](https://github.com/Hancapo/HikariHopper/pull/79))
- Switching between list and grid animates each item to its new place and keeps your scroll position. ([#80](https://github.com/Hancapo/HikariHopper/pull/80))
- Improved contrast throughout the app, especially on IPS screens. ([#81](https://github.com/Hancapo/HikariHopper/pull/81))
- The footer now shows the open game's edition mark and your selection, instead of a status light and key hints. ([#82](https://github.com/Hancapo/HikariHopper/pull/82))

### Fixed

- Images dragged into the texture editor from a browser or a zip file now import. ([#57](https://github.com/Hancapo/HikariHopper/pull/57))
- The changelog now scrolls with the mouse wheel anywhere over it. ([#60](https://github.com/Hancapo/HikariHopper/pull/60))
- Scrolling large text files no longer stutters. ([#61](https://github.com/Hancapo/HikariHopper/pull/61))
- Renaming a texture no longer loses its thumbnail. ([#65](https://github.com/Hancapo/HikariHopper/pull/65))
- Long paths no longer run under the refresh button. ([#74](https://github.com/Hancapo/HikariHopper/pull/74))
- Copy path inside a nested RPF now gives the path from the archive file on disk. ([#75](https://github.com/Hancapo/HikariHopper/pull/75))

### Removed

- Repair alpha edges, from the texture editor. ([#56](https://github.com/Hancapo/HikariHopper/pull/56))

## [0.0.3] - 2026-11-03

### Added

- Text viewer for TXT, XML, META, JSON, CSV, INI, LUA, LOG and DAT files, with syntax colours, line numbers, find, wrapping and zoom; CSV files open as a table. ([#46](https://github.com/Hancapo/HikariHopper/pull/46))
- Choose whether the last game reopens at startup, and clear the start page's recent games. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- HikariHopper's own logo and icon. ([#34](https://github.com/Hancapo/HikariHopper/pull/34))
- Dragging items out of the explorer shows what is being extracted and how many. ([#23](https://github.com/Hancapo/HikariHopper/pull/23), [#25](https://github.com/Hancapo/HikariHopper/pull/25))
- Game tabs show their edition as a V, grey for Legacy and green for Enhanced, and their path on hover. ([#26](https://github.com/Hancapo/HikariHopper/pull/26))
- Reorder tabs by dragging them; the others glide into place when one closes. ([#31](https://github.com/Hancapo/HikariHopper/pull/31), [#32](https://github.com/Hancapo/HikariHopper/pull/32))
- With a menu open, hovering another menu title opens that one. ([#42](https://github.com/Hancapo/HikariHopper/pull/42))

### Changed

- Redesigned About around the logo, with a GitHub link and the changelog. ([#51](https://github.com/Hancapo/HikariHopper/pull/51))
- Settings, About, Keyboard shortcuts and the texture editor now open inside the main window, and Settings shows each installation's status. ([#35](https://github.com/Hancapo/HikariHopper/pull/35), [#36](https://github.com/Hancapo/HikariHopper/pull/36))
- Regrouped the texture preview toolbar, with colour-coded channels and more zoom presets. ([#38](https://github.com/Hancapo/HikariHopper/pull/38))
- Option groups such as channels and list or grid are now switches. ([#40](https://github.com/Hancapo/HikariHopper/pull/40))
- The start page is now a game launcher, grouped by game and edition. ([#33](https://github.com/Hancapo/HikariHopper/pull/33))
- Menus now hang from their title and cast a soft shadow. ([#30](https://github.com/Hancapo/HikariHopper/pull/30))
- Tabs are named after the folder or RPF they show, and the folder tree and path bar no longer repeat the root. ([#43](https://github.com/Hancapo/HikariHopper/pull/43))

### Fixed

- A YDR without embedded textures no longer opens an empty texture editor. ([#48](https://github.com/Hancapo/HikariHopper/pull/48))
- Tab and Escape keep working in a dialog after clicking the title bar. ([#44](https://github.com/Hancapo/HikariHopper/pull/44))
- The folder tree no longer keeps the previous folder outlined after going back to the root. ([#45](https://github.com/Hancapo/HikariHopper/pull/45))
- About shows the real version and credits Avalonia instead of Qt. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- The folder tree only shows expand arrows on RPFs with something inside. ([#19](https://github.com/Hancapo/HikariHopper/pull/19))
- The mip slider jumps to where you click. ([#20](https://github.com/Hancapo/HikariHopper/pull/20))
- Switching mips and channels in the texture viewer is instant. ([#21](https://github.com/Hancapo/HikariHopper/pull/21), [#39](https://github.com/Hancapo/HikariHopper/pull/39))
- Zoom presets open under the zoom readout. ([#41](https://github.com/Hancapo/HikariHopper/pull/41))
- Opening a texture's context menu no longer makes the preview blink or reset to the first mip. ([#22](https://github.com/Hancapo/HikariHopper/pull/22))
- Long folder names in the folder tree end in an ellipsis. ([#27](https://github.com/Hancapo/HikariHopper/pull/27))
- Menu titles no longer keep a white line after closing, and only show focus from the keyboard. ([#29](https://github.com/Hancapo/HikariHopper/pull/29))
- Release builds no longer contain the build machine's folder paths. ([#18](https://github.com/Hancapo/HikariHopper/pull/18))

## [0.0.2] - 2026-11-02

### Changed

- Text now uses Avalonia's own rendering, keeping each font's true letter widths and line heights. ([#13](https://github.com/Hancapo/HikariHopper/pull/13))
- Releases are native builds and no longer need the .NET 10 Runtime. ([#14](https://github.com/Hancapo/HikariHopper/pull/14))

## [0.0.1] - 2026-11-02

### Added

- Explorer for GTA V Legacy and Enhanced game files, with nested RPF archives, a folder tree, list and grid views, search and tabs.
- Texture editor for YTD dictionaries and textures embedded in YDR drawables.

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.0.4...HEAD
[0.0.4]: https://github.com/Hancapo/HikariHopper/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
