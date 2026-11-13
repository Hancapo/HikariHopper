# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

### Fixed

### Removed

## [0.2.0] - 2026-11-12

### Added

- Edit and save text files in the text viewer, into their RPF or as loose files, keeping their encoding and line endings. ([#180](https://github.com/Hancapo/HikariHopper/pull/180))
- Vehicles opened from a game are painted as the game paints them, with Paint in the 3D viewer's footer to pick one of their colour combinations or any of the game's colours for each slot. ([#173](https://github.com/Hancapo/HikariHopper/pull/173), [#184](https://github.com/Hancapo/HikariHopper/pull/184), [#188](https://github.com/Hancapo/HikariHopper/pull/188), [#189](https://github.com/Hancapo/HikariHopper/pull/189), [#197](https://github.com/Hancapo/HikariHopper/pull/197))
- Click a model in the 3D viewer to pick a geometry, outlined and selected in the parts panel. ([#156](https://github.com/Hancapo/HikariHopper/pull/156))
- Edit a picked geometry's shader values in the 3D viewer, with undo and redo, and save them into the file's RPF or over the loose file. ([#157](https://github.com/Hancapo/HikariHopper/pull/157), [#158](https://github.com/Hancapo/HikariHopper/pull/158), [#161](https://github.com/Hancapo/HikariHopper/pull/161), [#192](https://github.com/Hancapo/HikariHopper/pull/192), [#198](https://github.com/Hancapo/HikariHopper/pull/198))
- Change a shader entry shared by several geometries for all of them or for the picked one alone. ([#159](https://github.com/Hancapo/HikariHopper/pull/159))
- Save a drawable, fragment or drawable dictionary as another file from the 3D viewer. ([#161](https://github.com/Hancapo/HikariHopper/pull/161), [#199](https://github.com/Hancapo/HikariHopper/pull/199))
- Extract a 3D model's embedded textures to DDS files from the 3D viewer. ([#161](https://github.com/Hancapo/HikariHopper/pull/161))
- The 3D viewer and the text viewer have File and Edit menus, as the texture editor has. ([#161](https://github.com/Hancapo/HikariHopper/pull/161))
- An axes gizmo in the 3D viewer's corner: click an axis to look along it, or drag it to orbit. ([#155](https://github.com/Hancapo/HikariHopper/pull/155))
- Pick a model's level of detail from a list that shows each level's triangles. ([#153](https://github.com/Hancapo/HikariHopper/pull/153))
- Fly the camera and frame the view in the 3D viewer for collisions and navmeshes too. ([#146](https://github.com/Hancapo/HikariHopper/pull/146))
- Resize textures by percentage, or with one press to a quarter, half or double their size. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Mipmaps can drop a texture's mip chain, keeping its full size. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))
- Long imports, extracts, saves and changes inside an RPF show their progress over the explorer or the texture editor, with Cancel. ([#177](https://github.com/Hancapo/HikariHopper/pull/177), [#178](https://github.com/Hancapo/HikariHopper/pull/178), [#179](https://github.com/Hancapo/HikariHopper/pull/179), [#191](https://github.com/Hancapo/HikariHopper/pull/191))
- The image viewer's status bar says how to zoom, pan and fit the image. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- A Licences button in About, with the notices of the code HikariHopper includes. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))

### Changed

- The 3D viewer has no toolbar: what's drawn over the model is a row at the top of the view, and the level of detail, Orbit | Fly and the time of day are in the footer. ([#153](https://github.com/Hancapo/HikariHopper/pull/153))
- The 3D viewer's time of day is a dial, with the sun or moon to drag round it. ([#154](https://github.com/Hancapo/HikariHopper/pull/154))
- A fragment's intact and damaged states and a model's embedded textures are rows in the 3D viewer's parts panel instead of toolbar buttons. ([#152](https://github.com/Hancapo/HikariHopper/pull/152))
- A collision's materials and a navmesh's colours are listed in the 3D viewer's parts panel, where each can be hidden. ([#148](https://github.com/Hancapo/HikariHopper/pull/148))
- A ped's parts in the 3D viewer are listed head to toe, each kind's variants in number order. ([#146](https://github.com/Hancapo/HikariHopper/pull/146))
- The shader values card opens from the parts panel's right-click menu, or with M. ([#172](https://github.com/Hancapo/HikariHopper/pull/172))
- A vehicle's paint in the 3D viewer shines as it does in the game, each slot with its colour's finish from carcols. ([#174](https://github.com/Hancapo/HikariHopper/pull/174))
- Paint, chrome and glass in the 3D viewer reflect the floor below the horizon. ([#175](https://github.com/Hancapo/HikariHopper/pull/175))
- Shadows in the 3D viewer are sharper close up, split into four cascades as GTA V's are. ([#163](https://github.com/Hancapo/HikariHopper/pull/163))
- Models open in the 3D viewer with the game's textures straight away: they are indexed while the game loads, and again in half the time after an archive changes. ([#164](https://github.com/Hancapo/HikariHopper/pull/164), [#187](https://github.com/Hancapo/HikariHopper/pull/187), [#193](https://github.com/Hancapo/HikariHopper/pull/193))
- While a game loads, a steam train runs over the caption in place of the sliding bar. ([#176](https://github.com/Hancapo/HikariHopper/pull/176), [#196](https://github.com/Hancapo/HikariHopper/pull/196))
- Importing images into a texture dictionary is much faster. ([#182](https://github.com/Hancapo/HikariHopper/pull/182))
- The keyboard shortcuts list covers the 3D viewer, the texture editor and the text and image viewers too. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The Properties sheet is as tall as what it shows. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The address bar names the game or RPF at its root. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The Type column shows only what a file is, without "(Compressed)". ([#183](https://github.com/Hancapo/HikariHopper/pull/183))
- Resize, Change format and Mipmaps show the texture now and after side by side, with the memory each takes. ([#149](https://github.com/Hancapo/HikariHopper/pull/149), [#150](https://github.com/Hancapo/HikariHopper/pull/150))
- Change format says what each format costs and keeps, and warns when it loses a texture's alpha or colour. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))
- Change format uses the filter last applied in Resize or Mipmaps instead of a choice of its own. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))
- With the aspect ratio locked, the other dimension follows as you type in the Resize dialog. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Resizing to a power of two is a button in the Resize dialog. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Renaming a texture says what's wrong with a name as you type it. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))
- Removing textures names them and shows the dictionary now and after, with a red Remove button. ([#145](https://github.com/Hancapo/HikariHopper/pull/145), [#151](https://github.com/Hancapo/HikariHopper/pull/151))
- Pasting an image shows its size, and whether it replaces the selected texture or adds a new one. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))
- Importing images whose names clash lists which are new and which replace a texture. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))

### Fixed

- Closing HikariHopper or a tab with unsaved files asks first, listing them all, where the changes were lost. ([#195](https://github.com/Hancapo/HikariHopper/pull/195))
- Saving into, renaming or deleting an RPF in the game folder no longer fails with "Access to the path is denied" right after the game opens. ([#185](https://github.com/Hancapo/HikariHopper/pull/185))
- A 3D model's embedded textures open again after a save into its RPF. ([#194](https://github.com/Hancapo/HikariHopper/pull/194))
- Save as in the texture editor keeps a name typed with another extension, and never replaces a file that wasn't picked. ([#199](https://github.com/Hancapo/HikariHopper/pull/199))
- Clicking HikariHopper's taskbar button minimizes it, and minimizing, restoring and maximizing animate as in other Windows 11 apps. ([#143](https://github.com/Hancapo/HikariHopper/pull/143))
- Navmeshes from the DLC packs open in the 3D viewer. ([#144](https://github.com/Hancapo/HikariHopper/pull/144))
- The 3D viewer's exposure no longer changes as the camera comes closer or goes further. ([#162](https://github.com/Hancapo/HikariHopper/pull/162))
- A bright model's glow in the 3D viewer fades smoothly into the sky instead of in rings. ([#160](https://github.com/Hancapo/HikariHopper/pull/160))
- Buttons that can't be used are clearly faded. ([#146](https://github.com/Hancapo/HikariHopper/pull/146))
- A right-click on a part in the parts panel opens its menu at once, with lines between its groups. ([#171](https://github.com/Hancapo/HikariHopper/pull/171))
- The parts panel's header reaches the panel's edge. ([#170](https://github.com/Hancapo/HikariHopper/pull/170))
- Text fields in the texture editor's dialogs are as tall as the dropdowns beside them. ([#145](https://github.com/Hancapo/HikariHopper/pull/145))
- The texture editor says "1 mip", not "1 mips", and shows no mip slider for a texture with only one. ([#145](https://github.com/Hancapo/HikariHopper/pull/145))
- The keyboard shortcuts list was missing Rename, Properties, Paste and typing an address. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The start page said Settings twice when no game was set up. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))

## [0.1.0] - 2026-11-09

### Added

- A 3D viewer for drawables (.ydr), fragments (.yft) and drawable dictionaries (.ydd), from Legacy and Enhanced. ([#91](https://github.com/Hancapo/HikariHopper/pull/91), [#94](https://github.com/Hancapo/HikariHopper/pull/94))
- Models in the 3D viewer are shaded with GTA V's own shaders, including their parallax and detail maps. ([#91](https://github.com/Hancapo/HikariHopper/pull/91), [#103](https://github.com/Hancapo/HikariHopper/pull/103), [#106](https://github.com/Hancapo/HikariHopper/pull/106), [#107](https://github.com/Hancapo/HikariHopper/pull/107))
- Vehicles in the 3D viewer are shaded with GTA V's vehicle shaders, liveries included. ([#98](https://github.com/Hancapo/HikariHopper/pull/98), [#99](https://github.com/Hancapo/HikariHopper/pull/99))
- Terrain in the 3D viewer is shaded with GTA V's terrain shaders. ([#101](https://github.com/Hancapo/HikariHopper/pull/101), [#104](https://github.com/Hancapo/HikariHopper/pull/104))
- Peds in the 3D viewer are shaded with GTA V's ped shaders, hair included. ([#139](https://github.com/Hancapo/HikariHopper/pull/139))
- Models in the 3D viewer are lit as in the game, with its sun, sky, shadows, reflections and bloom. ([#96](https://github.com/Hancapo/HikariHopper/pull/96), [#97](https://github.com/Hancapo/HikariHopper/pull/97), [#127](https://github.com/Hancapo/HikariHopper/pull/127))
- Pick the time of day in the 3D viewer, with the sun and moon on the game's own path across the sky. ([#105](https://github.com/Hancapo/HikariHopper/pull/105), [#134](https://github.com/Hancapo/HikariHopper/pull/134))
- The 3D viewer finds a model's textures where the game does, mods' RPFs included. ([#94](https://github.com/Hancapo/HikariHopper/pull/94), [#102](https://github.com/Hancapo/HikariHopper/pull/102), [#122](https://github.com/Hancapo/HikariHopper/pull/122))
- Missing textures show as a magenta and black checkerboard in the 3D viewer. ([#124](https://github.com/Hancapo/HikariHopper/pull/124))
- A parts panel in the 3D viewer to show or hide a model's parts, in sections such as a vehicle's body, wheels and extras or a ped's head and torso. ([#109](https://github.com/Hancapo/HikariHopper/pull/109), [#137](https://github.com/Hancapo/HikariHopper/pull/137), [#138](https://github.com/Hancapo/HikariHopper/pull/138))
- Look at a model's lower levels of detail in the 3D viewer. ([#109](https://github.com/Hancapo/HikariHopper/pull/109))
- Show a vehicle damaged in the 3D viewer. ([#118](https://github.com/Hancapo/HikariHopper/pull/118))
- Show a model's skeleton, collision and lights over it in the 3D viewer. ([#112](https://github.com/Hancapo/HikariHopper/pull/112), [#119](https://github.com/Hancapo/HikariHopper/pull/119), [#140](https://github.com/Hancapo/HikariHopper/pull/140))
- A free camera in the 3D viewer to fly around and into a model. ([#111](https://github.com/Hancapo/HikariHopper/pull/111))
- A model's triangle, vertex and edge counts in the 3D viewer's status bar. ([#108](https://github.com/Hancapo/HikariHopper/pull/108))
- Open a model's embedded textures from the 3D viewer, and see your saved edits on the model. ([#110](https://github.com/Hancapo/HikariHopper/pull/110), [#131](https://github.com/Hancapo/HikariHopper/pull/131))
- Smoother model edges in the 3D viewer, with anti-aliasing. ([#136](https://github.com/Hancapo/HikariHopper/pull/136))
- Hovering the maximize button on Windows 11 shows the snap layouts. ([#93](https://github.com/Hancapo/HikariHopper/pull/93))

### Changed

- Drawables (.ydr) open in the 3D viewer instead of the texture editor. ([#91](https://github.com/Hancapo/HikariHopper/pull/91))
- The 3D viewer opens faster and shows a model before its textures are found. ([#100](https://github.com/Hancapo/HikariHopper/pull/100), [#123](https://github.com/Hancapo/HikariHopper/pull/123))
- The texture editor shows a picked texture much sooner, without flashing "Decoding texture…". ([#130](https://github.com/Hancapo/HikariHopper/pull/130))
- The delete, new folder, rename and unsaved changes dialogs, and the texture editor's tool dialogs, match the app's other sheets. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The keyboard shortcuts list and the start page name actions as the menus do. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))

### Fixed

- Dropping files with names already in the folder asks whether to replace, skip or keep both, instead of cancelling the drop. ([#121](https://github.com/Hancapo/HikariHopper/pull/121))
- Undo works right after resizing a texture or using a texture editor dialog, without clicking a texture first. ([#125](https://github.com/Hancapo/HikariHopper/pull/125))
- The search box shows its subfolders toggle only when searching a folder. ([#114](https://github.com/Hancapo/HikariHopper/pull/114))
- The start page names a recent installation by its edition, Legacy or Enhanced, instead of its folder. ([#120](https://github.com/Hancapo/HikariHopper/pull/120))
- The explorer's status bar says "1 item", not "1 items". ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The keyboard shortcuts list says Ctrl+C copies the selection, as it does. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The text viewer's toolbar buttons light up on hover like the other toolbars. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))

## [0.0.5] - 2026-11-06

### Added

- HikariHopper updates itself: new versions are offered with what changed in them, and install with a restart. ([#87](https://github.com/Hancapo/HikariHopper/pull/87))
- The update check at startup can be turned off in Settings. ([#87](https://github.com/Hancapo/HikariHopper/pull/87))
- An Updates page in Settings, to check for updates. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))
- A 3D viewer for collisions (.ybn) and navmeshes (.ynv), which shows them where they are in the world. ([#90](https://github.com/Hancapo/HikariHopper/pull/90))
- Navmeshes in the 3D viewer show their kinds of ground, climbs, drops, ladders and cover points, with a legend. ([#90](https://github.com/Hancapo/HikariHopper/pull/90))

### Changed

- The in-app changelog now loads from GitHub, so it stays up to date between releases. ([#86](https://github.com/Hancapo/HikariHopper/pull/86))
- The release zip updates itself, so it needs a folder you can write to. ([#87](https://github.com/Hancapo/HikariHopper/pull/87))
- Settings has a page per group, picked from a rail with icons. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))
- Settings that are on or off use Off/On switches. ([#88](https://github.com/Hancapo/HikariHopper/pull/88))
- The panels are taller, and the status bar is centred under them. ([#89](https://github.com/Hancapo/HikariHopper/pull/89))

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

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Hancapo/HikariHopper/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Hancapo/HikariHopper/compare/v0.0.5...v0.1.0
[0.0.5]: https://github.com/Hancapo/HikariHopper/compare/v0.0.4...v0.0.5
[0.0.4]: https://github.com/Hancapo/HikariHopper/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
