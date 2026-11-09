# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The 3D viewer opens drawables (.ydr), textured and shaded as GTA V shades them, from Legacy and Enhanced. ([#91](https://github.com/Hancapo/HikariHopper/pull/91))
- The 3D viewer opens fragments (.yft), such as vehicles with their wheels, and drawable dictionaries (.ydd). ([#94](https://github.com/Hancapo/HikariHopper/pull/94))
- The 3D viewer finds a model's textures in the texture dictionaries beside it, such as a vehicle's shared and interior ones. ([#94](https://github.com/Hancapo/HikariHopper/pull/94))
- Vehicles in the 3D viewer are shaded with GTA V's vehicle shaders: paint, body parts, tyres and interiors, with paint slots filled as the game fills them without a vehicle's colours. ([#99](https://github.com/Hancapo/HikariHopper/pull/99))
- Models in the 3D viewer are lit as GTA V lights them on a clear afternoon, with the game's sun, ambient light, exposure and tone map, and the sun's shadows on them and on the ground. ([#96](https://github.com/Hancapo/HikariHopper/pull/96))
- Models in the 3D viewer reflect themselves and GTA V's sky, as the game draws its reflection map from the camera, and are exposed for what's in view, as the game exposes its frame. ([#97](https://github.com/Hancapo/HikariHopper/pull/97))
- Each vehicle shader in the 3D viewer writes its specular as its own program does, and liveries and decals lie over the paint. ([#98](https://github.com/Hancapo/HikariHopper/pull/98))
- The 3D viewer opens models faster: its GPU and shaders are made once, in the background after the app starts, and a model is read while the viewer opens instead of freezing the window. ([#100](https://github.com/Hancapo/HikariHopper/pull/100))
- Terrain in the 3D viewer is shaded with GTA V's terrain shaders: its four ground layers blended by the vertex colours or a lookup map, with their bumps and tints. ([#101](https://github.com/Hancapo/HikariHopper/pull/101))
- The 3D viewer finds a map model's textures where the game does: in the dictionary its archetype names and that dictionary's parents, such as the shared road and terrain dictionaries. The game's texture parents are worked out in the background when the game loads, and kept until its archives change. ([#102](https://github.com/Hancapo/HikariHopper/pull/102))
- The 3D viewer draws the detail maps that props and buildings tile over their surfaces, found in the game's mapdetail.ytd. ([#103](https://github.com/Hancapo/HikariHopper/pull/103))
- Terrain in the 3D viewer is drawn with the rest of GTA V's terrain shaders, including their parallax near the camera, specular maps and layers on a second UV set; blended ground mixes its layers as the game does, and the terrain shades itself from the sun as the game does. ([#104](https://github.com/Hancapo/HikariHopper/pull/104))
- A time-of-day slider in the 3D viewer's status bar, to see a model in the game's light at any hour. ([#105](https://github.com/Hancapo/HikariHopper/pull/105))
- The 3D viewer shades surfaces whose shine comes from their colour as GTA V does, such as tree bark and some lit signs. ([#106](https://github.com/Hancapo/HikariHopper/pull/106))
- The 3D viewer draws the parallax of walls, floors and decals that have it, with its self-shadow, as GTA V does. ([#107](https://github.com/Hancapo/HikariHopper/pull/107))
- A toolbar in the 3D viewer for models, to turn shadows on or off and frame the model, and the model's triangle, vertex and edge counts in its status bar. ([#108](https://github.com/Hancapo/HikariHopper/pull/108))
- A parts panel in the 3D viewer, to show or hide a model's drawables, parts and materials, and a level of detail picker to look at its lower-detail versions. ([#109](https://github.com/Hancapo/HikariHopper/pull/109))
- A textures button in the 3D viewer's toolbar that opens a model's embedded textures in the texture viewer. ([#110](https://github.com/Hancapo/HikariHopper/pull/110))
- A free camera in the 3D viewer for models, to fly around and into a model with W, A, S, D, Q and E, look by dragging, and set its speed with the wheel. ([#111](https://github.com/Hancapo/HikariHopper/pull/111))
- The 3D viewer draws a model's skeleton, collision bounds and lights over it, each turned on from the toolbar. ([#112](https://github.com/Hancapo/HikariHopper/pull/112))
- Bloom in the 3D viewer, as GTA V Enhanced draws it: emissive surfaces and other bright parts of a model glow, more strongly at night, as the game's timecycle sets it for each hour. ([#127](https://github.com/Hancapo/HikariHopper/pull/127))
- The sun and moon in the 3D viewer cross the sky on the game's own path, read from the game's time.xml: the sun rises due east at 06:00, is highest at 13:00 and sets due west at 20:00, and the moon lights the night from 21:45 to 04:45. ([#134](https://github.com/Hancapo/HikariHopper/pull/134))
- The 3D viewer is anti-aliased with CMAA2: the stair steps along a model's edges and the grid's lines are smoothed. ([#136](https://github.com/Hancapo/HikariHopper/pull/136))
- Textures edited from the 3D viewer's textures button show on the model as soon as the texture editor closes after saving, a .ydr's saved into it and a fragment's or drawable dictionary's over its own. ([#131](https://github.com/Hancapo/HikariHopper/pull/131))
- Peds in the 3D viewer are shaded with GTA V's ped shaders: skin and clothes with the game's rim light, specular and ambient occlusion, their simpler lower-detail parts, and hair with its highlights along the strands, covering what's behind it as much as its alpha says, as the game's hair does. Cut-out surfaces, such as hair, leaves and fences, cast shadows only where they're solid. ([#139](https://github.com/Hancapo/HikariHopper/pull/139))
- Hovering the maximize button on Windows 11 shows the snap layouts. ([#93](https://github.com/Hancapo/HikariHopper/pull/93))

### Changed

- Double-clicking a drawable (.ydr) opens it in the 3D viewer instead of the texture editor. ([#91](https://github.com/Hancapo/HikariHopper/pull/91))
- The 3D viewer picks a model's level of detail from segments, shown only when it has more than one, and its time of day from a dropdown on the toolbar; its textures button is a plain icon, and its status bar no longer shows the GPU, the material count or the size. ([#116](https://github.com/Hancapo/HikariHopper/pull/116))
- The 3D viewer's parts panel slides open and closed from an arrow on its edge, which also resizes it, and its boxes can be set by pressing one and dragging over the rest. ([#117](https://github.com/Hancapo/HikariHopper/pull/117))
- The 3D viewer's parts panel lists a fragment's children, such as a vehicle's doors, bonnet and windows, and its extras, each of which can be hidden, and a toolbar button shows a fragment damaged. ([#118](https://github.com/Hancapo/HikariHopper/pull/118))
- The 3D viewer draws a model's collision solid, in its materials' colours over the model, as well as its bounds' edges. ([#119](https://github.com/Hancapo/HikariHopper/pull/119))
- The 3D viewer finds a model's textures through mods too: RPFs in the game folder that the game doesn't load itself, such as fix/touhou.rpf, over the game's own. ([#122](https://github.com/Hancapo/HikariHopper/pull/122))
- The 3D viewer shows a model at once, without waiting for the game's textures to be found, and adds them when they are. ([#123](https://github.com/Hancapo/HikariHopper/pull/123))
- The 3D viewer draws a colour map that a model names but that isn't found as a magenta and black checkerboard, so missing textures stand out. ([#124](https://github.com/Hancapo/HikariHopper/pull/124))
- The 3D viewer's skeleton, collision and lights buttons float over the 3D view in a tool strip on its left edge, and the parts panel's arrow floats at its top left; the collision is drawn only solid, with its edges, without a separate bounds wireframe. ([#132](https://github.com/Hancapo/HikariHopper/pull/132))
- The delete, new folder, rename and unsaved changes dialogs and the texture editor's tool dialogs look like the app's other sheets: rounded with a shadow, a close button in the header, and the same header and footer. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The 3D viewer's status bar is as tall as the other viewers', and its parts panel's header matches the explorer's list header. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The keyboard shortcuts list and the start page name actions as the menus do. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The 3D viewer's parts panel is a tree with dotted guides, an eye to show or hide each row and a badge for what it is; rows are selected apart from what is shown, with the mouse or the keyboard, narrowed by a filter, and a right click shows only, hides, frames or copies what is selected. ([#137](https://github.com/Hancapo/HikariHopper/pull/137))
- The 3D viewer's parts panel shows a vehicle's body, wheels and other groups in sections, with its extras as numbered toggles, and a drawable dictionary's drawables by what they are (Head, Torso…), one of each at a time or all of them; a ped's drawables are named for what they are, not all for their dictionary. ([#138](https://github.com/Hancapo/HikariHopper/pull/138))
- The 3D viewer's skeleton, collision and lights buttons are joined into one column over the 3D view, as in Blender: one that's on is filled with the accent colour, and one the model has nothing for is faded out. ([#140](https://github.com/Hancapo/HikariHopper/pull/140))

### Fixed

- Tooltips over the 3D viewer are as tall as their text instead of a fixed 39 pixels, and a disabled button's tooltip says why it's off. ([#140](https://github.com/Hancapo/HikariHopper/pull/140))
- The 3D viewer's darkest shades are as bright as in the game: the tone-mapped colour is written with the game's gamma (1/2.2) instead of the sRGB curve. ([#135](https://github.com/Hancapo/HikariHopper/pull/135))
- The explorer's status bar says "1 item", not "1 items". ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The keyboard shortcuts list says Ctrl+C copies the selection, as it does. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- The text viewer's toolbar buttons brighten on hover like the other toolbars'. ([#133](https://github.com/Hancapo/HikariHopper/pull/133))
- A model opened from search results in the 3D viewer finds its textures as it does from its folder, and its textures button works. ([#113](https://github.com/Hancapo/HikariHopper/pull/113))
- In the 3D viewer, bumps on the side of a surface facing away from the sun no longer catch sunlight. ([#126](https://github.com/Hancapo/HikariHopper/pull/126))
- Clicking outside the 3D viewer's time of day dropdown closes it without sending HikariHopper behind other windows. ([#128](https://github.com/Hancapo/HikariHopper/pull/128))
- Tooltips in the 3D viewer show over the 3D view instead of behind it. ([#129](https://github.com/Hancapo/HikariHopper/pull/129))
- Decals in the 3D viewer fade out by their vertex alpha as GTA V blends them, instead of covering what they lie on with hard edges. ([#115](https://github.com/Hancapo/HikariHopper/pull/115))
- The search box shows its subfolders toggle only when searching a folder, not an RPF or the whole game. ([#114](https://github.com/Hancapo/HikariHopper/pull/114))
- The start page names a recently opened installation for its edition, Legacy or Enhanced, rather than its folder. ([#120](https://github.com/Hancapo/HikariHopper/pull/120))
- Dropping files whose names are already in the folder asks whether to replace them, skip them or keep both, instead of cancelling the whole drop; replacing a folder merges into it. ([#121](https://github.com/Hancapo/HikariHopper/pull/121))
- Ctrl+Z undoes a resize, or any change made from a texture editor dialog, right after it, instead of only after clicking a texture. ([#125](https://github.com/Hancapo/HikariHopper/pull/125))

### Removed

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

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.0.5...HEAD
[0.0.5]: https://github.com/Hancapo/HikariHopper/compare/v0.0.4...v0.0.5
[0.0.4]: https://github.com/Hancapo/HikariHopper/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
