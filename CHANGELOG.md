# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Import images… in the texture editor's File menu (Ctrl+I) adds images as new textures named after their files. ([#57](https://github.com/Hancapo/HikariHopper/pull/57))
- An image viewer: double-click a DDS, PNG, JPEG, BMP, TGA or WebP file to view it over the explorer, with its channels, zoom and, for a DDS, its mips. ([#62](https://github.com/Hancapo/HikariHopper/pull/62))
- Extract… copies the selected files and folders to a folder you pick, and Extract all content… writes an RPF's whole content into a folder named after it. ([#68](https://github.com/Hancapo/HikariHopper/pull/68))
- Copy (Ctrl+C) and Paste (Ctrl+V) work with files and folders, in and out of RPFs and to and from Windows Explorer; a pasted name already there gets a number. ([#69](https://github.com/Hancapo/HikariHopper/pull/69))
- Rename… (F2) renames a file or folder, on disk or inside an RPF. ([#70](https://github.com/Hancapo/HikariHopper/pull/70))
- Properties (Alt+Enter) shows where a file or folder is and its size, the contents of a texture dictionary, and an RPF's encryption and file count. ([#71](https://github.com/Hancapo/HikariHopper/pull/71))
- The address bar can be copied and pasted: click its empty part or press Ctrl+L to edit the path, and type or paste a folder, an RPF or a path inside one to go there; right-click it for Copy address and Paste and go. ([#75](https://github.com/Hancapo/HikariHopper/pull/75))
- The search box searches the folder, the RPF you're in or the whole game, inside every RPF and with subfolders on request, by plain text, wildcards such as *.ydr or a regular expression; results open, copy, extract, rename and delete as in a folder, and Open location shows a result where it lives. ([#77](https://github.com/Hancapo/HikariHopper/pull/77))

### Changed

- The window no longer has a blue border. ([#55](https://github.com/Hancapo/HikariHopper/pull/55))
- The texture context menu is regrouped with icons, and Resize and Format open their options. ([#56](https://github.com/Hancapo/HikariHopper/pull/56))
- The texture editor's and the text viewer's headers show the file's icon and name, the way the explorer lists it. ([#63](https://github.com/Hancapo/HikariHopper/pull/63))
- Image and text files have their own icons and types in the explorer, such as PNG Image and DDS Texture. ([#63](https://github.com/Hancapo/HikariHopper/pull/63))
- The changelog in the app lists only released versions; test builds also show the changes not released yet. ([#64](https://github.com/Hancapo/HikariHopper/pull/64))
- Files inside an archive show the size they take in it, and compressed ones say so in their type. ([#66](https://github.com/Hancapo/HikariHopper/pull/66))
- The explorer's context menu fits what you right-click: an item gets Open, Copy name, Copy path and Delete, and empty space gets New and Refresh. ([#67](https://github.com/Hancapo/HikariHopper/pull/67))
- Ctrl+C copies the selected files; Copy name has no shortcut, and folders can be dropped onto the explorer too. ([#69](https://github.com/Hancapo/HikariHopper/pull/69))
- The window title and splash screen read Modding Toolkit, and the start-up and empty-state messages refer to your game. ([#72](https://github.com/Hancapo/HikariHopper/pull/72))
- A new app icon: a railway conductor's cap on a brass tile. ([#73](https://github.com/Hancapo/HikariHopper/pull/73))

### Fixed

- Images dragged into the texture editor from a browser or out of a zip are imported. ([#57](https://github.com/Hancapo/HikariHopper/pull/57))
- The changelog scrolls with the mouse wheel anywhere over it, not only over its text. ([#60](https://github.com/Hancapo/HikariHopper/pull/60))
- Scrolling through big text files in the text viewer is smooth, without stutter. ([#61](https://github.com/Hancapo/HikariHopper/pull/61))
- Renaming a texture in the texture editor keeps its thumbnail. ([#65](https://github.com/Hancapo/HikariHopper/pull/65))
- A long path no longer runs under the refresh button: its leading folders fold into a … menu, and a name too long to fit is cut short. ([#74](https://github.com/Hancapo/HikariHopper/pull/74))
- Copy path inside a nested RPF gives the path from the archive's file on disk. ([#75](https://github.com/Hancapo/HikariHopper/pull/75))

### Removed

- Repair alpha edges, from the texture editor. ([#56](https://github.com/Hancapo/HikariHopper/pull/56))

## [0.0.3] - 2026-11-03

### Added

- A read-only text viewer: double-click a TXT, XML, META, JSON, CSV, INI, LUA, LOG or DAT file to open it over the explorer, with syntax colours, line numbers, find (Ctrl+F), wrapping and zoom. CSV files open as a table, and can be switched to text. ([#46](https://github.com/Hancapo/HikariHopper/pull/46))
- Settings can turn off reopening the last game at startup, and clear the recent games the start page lists. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- HikariHopper has its own logo and icon, on the exe, the taskbar, the title bars, About and the splash screen. ([#34](https://github.com/Hancapo/HikariHopper/pull/34))
- Dragging entries out of the explorer shows a cursor with what is being extracted and how many items. ([#23](https://github.com/Hancapo/HikariHopper/pull/23), [#25](https://github.com/Hancapo/HikariHopper/pull/25))
- Game tabs show their edition with a V, grey for Legacy and green for Enhanced, and their path on hover. ([#26](https://github.com/Hancapo/HikariHopper/pull/26))
- Tabs can be reordered by dragging them along the tab strip, and the others glide into place when one closes. ([#31](https://github.com/Hancapo/HikariHopper/pull/31), [#32](https://github.com/Hancapo/HikariHopper/pull/32))
- While a menu is open, pointing at another menu title opens that menu, in the explorer and in the texture editor. ([#42](https://github.com/Hancapo/HikariHopper/pull/42))

### Changed

- About is redone around the logo, with a link to GitHub, and the changelog can be read in the app. ([#51](https://github.com/Hancapo/HikariHopper/pull/51))
- Settings, About, Keyboard shortcuts and the texture editor open inside the main window instead of as separate windows. Settings lists each game's installations with a clear status. ([#35](https://github.com/Hancapo/HikariHopper/pull/35), [#36](https://github.com/Hancapo/HikariHopper/pull/36))
- The texture preview's toolbar is regrouped, with each channel letter in its own colour, and its zoom readout offers presets from 25% to 800%. ([#38](https://github.com/Hancapo/HikariHopper/pull/38))
- Option groups (channels, Smooth / Pixels, Fit / 1:1, List / Grid) are switches. ([#40](https://github.com/Hancapo/HikariHopper/pull/40))
- The start page is a game launcher: the games to open, grouped by game with their editions, and a standalone RPF. ([#33](https://github.com/Hancapo/HikariHopper/pull/33))
- Menus hang from the title that opened them, and cast a soft shadow. ([#30](https://github.com/Hancapo/HikariHopper/pull/30))
- Tabs are named after the folder or RPF they are in, and Home at a game's root. The folder tree and the path bar no longer repeat the root. ([#43](https://github.com/Hancapo/HikariHopper/pull/43))

### Fixed

- A YDR with no embedded textures no longer opens an empty texture editor. ([#48](https://github.com/Hancapo/HikariHopper/pull/48))
- Tab and Escape keep working in a dialog after a click on the title bar. ([#44](https://github.com/Hancapo/HikariHopper/pull/44))
- The folder tree no longer keeps the folder you left outlined after going back to the root. ([#45](https://github.com/Hancapo/HikariHopper/pull/45))
- About shows the app's real version, and credits Avalonia instead of Qt. ([#35](https://github.com/Hancapo/HikariHopper/pull/35))
- The folder tree shows an expand arrow only on RPFs that have folders or other RPFs inside. ([#19](https://github.com/Hancapo/HikariHopper/pull/19))
- The mip slider jumps to where it is clicked. ([#20](https://github.com/Hancapo/HikariHopper/pull/20))
- Switching mips and channels in the texture viewer is instant. ([#21](https://github.com/Hancapo/HikariHopper/pull/21), [#39](https://github.com/Hancapo/HikariHopper/pull/39))
- The zoom presets open under the zoom readout. ([#41](https://github.com/Hancapo/HikariHopper/pull/41))
- Opening a texture's context menu no longer makes the preview blink or jump back to the first mip. ([#22](https://github.com/Hancapo/HikariHopper/pull/22))
- Long folder names in the folder tree end in an ellipsis. ([#27](https://github.com/Hancapo/HikariHopper/pull/27))
- Menu titles no longer keep a white line after their menu closes, and show focus only when it comes from the keyboard. ([#29](https://github.com/Hancapo/HikariHopper/pull/29))
- Release builds no longer contain the build machine's folder paths. ([#18](https://github.com/Hancapo/HikariHopper/pull/18))

## [0.0.2] - 2026-11-02

### Changed

- Text uses Avalonia's own rendering, keeping each font's letter widths and line heights instead of rounding them the way Qt does. ([#13](https://github.com/Hancapo/HikariHopper/pull/13))
- Releases are native builds and no longer need the .NET 10 Runtime. ([#14](https://github.com/Hancapo/HikariHopper/pull/14))

## [0.0.1] - 2026-11-02

### Added

- RPF explorer for GTA V Legacy and Enhanced, with nested archives, a folder tree, list and grid views, search and tabs.
- Texture editor for YTD dictionaries and textures embedded in YDR drawables.

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.0.3...HEAD
[0.0.3]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
