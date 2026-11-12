# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Edit text files in the text viewer and save them with Ctrl+S, into their RPF or over the loose file, in their own encoding and line endings, or as another file with Save as; undo and redo, cut and paste. The title marks unsaved changes, and closing asks whether to save them. ([#180](https://github.com/Hancapo/HikariHopper/pull/180))
- Importing, pasting, copying out of an RPF, extracting, deleting and any change inside an RPF show the loading train over the tab once they take a moment, with the file in hand and a bar counting the files, or the share of an RPF written. Cancel stops them: an import or any change inside an RPF leaves things as they were, an extract keeps the files it wrote and a delete keeps in the Recycle Bin what it moved there; the status says which. ([#179](https://github.com/Hancapo/HikariHopper/pull/179))
- Resizing textures, recalculating their mipmaps, changing their format or compression, extracting several to DDS files and saving the dictionary show the loading train over the texture editor once they take a moment, with a bar counting the textures and Cancel where it can stop: a cancelled change keeps nothing, a cancelled extract keeps the files it wrote. Extracting no longer freezes the window. ([#178](https://github.com/Hancapo/HikariHopper/pull/178))
- Importing images into a texture dictionary shows the loading train over the editor once it takes a moment, with the file being read, a progress bar counting the files, and Cancel, which stops the import without adding anything. ([#177](https://github.com/Hancapo/HikariHopper/pull/177))
- Vehicles opened from a game are painted in the 3D viewer as the game paints them: in the first of their colour combinations, with Paint in the footer to pick another combination or any of the game's colours for each of the six paint slots. ([#173](https://github.com/Hancapo/HikariHopper/pull/173))
- Fly the camera and frame the view in the 3D viewer for collisions and navmeshes too. ([#146](https://github.com/Hancapo/HikariHopper/pull/146))
- Pick a model's level of detail from a list that shows each level's triangles, and turn the mesh counts on or off; the viewer remembers which. ([#153](https://github.com/Hancapo/HikariHopper/pull/153))
- An axes gizmo in the 3D viewer's top right corner: click an axis to look from it, again for the other side, or drag it to orbit. ([#155](https://github.com/Hancapo/HikariHopper/pull/155))
- Click a model in the 3D viewer to pick a geometry: its row is selected in the parts panel and it is outlined; what is selected in the panel is outlined too. ([#156](https://github.com/Hancapo/HikariHopper/pull/156))
- Edit a picked geometry's shader values in the 3D viewer, on a card beside its row in the parts panel: drag or type a number to see it at once, reset one value or all of them, and undo with Ctrl+Z. ([#157](https://github.com/Hancapo/HikariHopper/pull/157), [#159](https://github.com/Hancapo/HikariHopper/pull/159))
- Save the shader values changed in the 3D viewer with Ctrl+S, into the file's RPF or over the loose file; the title marks unsaved changes, and closing asks whether to save them. ([#158](https://github.com/Hancapo/HikariHopper/pull/158))
- A shader entry shared by several geometries can be changed for all of them or for the picked one alone, which then gets an entry of its own; the card says what else the geometry is drawn on. ([#159](https://github.com/Hancapo/HikariHopper/pull/159))
- The 3D viewer and the text viewer have File and Edit menus, as the texture editor has. ([#161](https://github.com/Hancapo/HikariHopper/pull/161))
- Save a drawable, fragment or drawable dictionary as another file from the 3D viewer, with its shader values as changed; one in an RPF can be saved out this way. ([#161](https://github.com/Hancapo/HikariHopper/pull/161))
- Redo a change to the 3D viewer's shader values with Ctrl+Y or Ctrl+Shift+Z. ([#161](https://github.com/Hancapo/HikariHopper/pull/161))
- Extract a 3D model's embedded textures to DDS files from the 3D viewer. ([#161](https://github.com/Hancapo/HikariHopper/pull/161))

### Changed

- Importing images into a texture dictionary is much faster: several are read and compressed at once, and the compressor runs at a quality whose difference from its best is too small to see, so twelve 2048² images take about 2 seconds instead of over a minute. ([#182](https://github.com/Hancapo/HikariHopper/pull/182))
- While a game loads, a steam train runs along a short track over the caption, its wheels and rods turning and its stack puffing smoke, in place of a sliding bar; it stands still when Windows turns animations off. ([#176](https://github.com/Hancapo/HikariHopper/pull/176))
- A vehicle's paint in the 3D viewer shines as its colour does in the game: each slot takes its colour's finish from carcols, so metallics are glossy and reflect sharply, mattes are dull and chrome is a mirror. ([#174](https://github.com/Hancapo/HikariHopper/pull/174))
- Paint, chrome and glass in the 3D viewer reflect the floor the view shows, its colour and grid, below the horizon, instead of a plain grey ground. ([#175](https://github.com/Hancapo/HikariHopper/pull/175))
- The 3D viewer's shader values card opens from the parts panel's right-click menu, with Shader values…, or with M, instead of with every geometry picked; open, it follows the pick. ([#172](https://github.com/Hancapo/HikariHopper/pull/172))
- A ped's parts in the 3D viewer are listed head to toe, with each kind's variants in number order. ([#146](https://github.com/Hancapo/HikariHopper/pull/146))
- A collision's materials, and what a navmesh's colours mean, are listed in the 3D viewer's parts panel, where each can be hidden or found, instead of in a bar under the view. ([#148](https://github.com/Hancapo/HikariHopper/pull/148))
- A fragment's intact and damaged states are rows in the 3D viewer's parts panel, each with its parts, picked as a ped's variants are; its embedded textures are a row at the panel's foot. Both were toolbar buttons. ([#152](https://github.com/Hancapo/HikariHopper/pull/152))
- The 3D viewer has no toolbar: what is drawn over the model (shadows, skeleton, collision, lights) is a row at the top of the view, and the level of detail, the mesh counts, Orbit | Fly and the time of day are in the footer. The parts panel opens when asked for. ([#153](https://github.com/Hancapo/HikariHopper/pull/153))
- The 3D viewer's time of day is a dial of the day, with noon at the top and the sun or moon to drag round it, in place of a slider; the time button shows it small. ([#154](https://github.com/Hancapo/HikariHopper/pull/154))

- Shadows in the 3D viewer are sharper close up: the sun's shadow is split into four cascades by distance from the camera, as GTA V's is, instead of one map over the whole model. ([#163](https://github.com/Hancapo/HikariHopper/pull/163))
- The Type column shows only what a file is, without "(Compressed)" after the files an RPF keeps compressed. ([#183](https://github.com/Hancapo/HikariHopper/pull/183))
### Fixed

- Saving a 3D model's shader values right after saving its embedded textures no longer puts the old textures back, and a save no longer loses a geometry's own shader entry when the game's textures arrive at the same time; once a model is saved as another file, T and the parts panel no longer open the original's textures. ([#192](https://github.com/Hancapo/HikariHopper/pull/192))
- A 3D model's embedded textures open again with T after a save into its RPF, where they failed with "Cannot access a disposed object". ([#194](https://github.com/Hancapo/HikariHopper/pull/194))
- A vehicle whose game tables finished just as it appeared in the 3D viewer is painted and has its paint well, where it stayed grey. ([#197](https://github.com/Hancapo/HikariHopper/pull/197))
- A vehicle opened while the game's texture tables were still being worked out (after the game or the app changed) took several seconds to appear, waiting for its paint; it now appears at once in the game's grey and takes its colours when the tables are ready. ([#184](https://github.com/Hancapo/HikariHopper/pull/184))
- Buttons that can't be used are clearly faded, such as Back with no history or the 3D viewer's textures button on a model without any. ([#146](https://github.com/Hancapo/HikariHopper/pull/146))
- Clicking HikariHopper's taskbar button minimizes it, as it does for other apps. ([#143](https://github.com/Hancapo/HikariHopper/pull/143))
- HikariHopper animates minimizing, restoring and maximizing as other Windows 11 apps do. ([#143](https://github.com/Hancapo/HikariHopper/pull/143))
- Navmeshes from the DLC packs open in the 3D viewer, in Legacy and Enhanced, not only the main archives' ones. ([#144](https://github.com/Hancapo/HikariHopper/pull/144))
- The image viewer's status bar says how to zoom, pan and fit the image, as the texture editor's does. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- A Licences button in About, which opens the notices of the code HikariHopper includes. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))


- The keyboard shortcuts list covers the 3D viewer, the texture editor and the text and image viewers too, in columns. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The Properties sheet is as tall as what it shows, where a file's few rows left it mostly empty. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The address bar names the game or RPF at its root, where it was empty. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))


- The keyboard shortcuts list was missing Rename, Properties, Paste and typing an address. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- The start page said Settings twice when no game was set up. ([#147](https://github.com/Hancapo/HikariHopper/pull/147))
- Resize a texture by percentage, or with one press to a quarter, half or double its size. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Resizing several textures by percentage scales each from its own size. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Mipmaps can drop a texture's mip chain, leaving its full size alone. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))


- Removing a texture in the texture editor asks with a red Remove button, like deleting a file. ([#145](https://github.com/Hancapo/HikariHopper/pull/145))
- With the aspect ratio locked, the other dimension follows as you type in the Resize dialog. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- The Resize dialog shows the texture now and after side by side, with the memory each takes, and says when the new size is not a power of two. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Resizing to a power of two is a button in the Resize dialog. ([#149](https://github.com/Hancapo/HikariHopper/pull/149))
- Change format says what each format costs and keeps, and when the one picked loses the alpha or colour a texture has. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))
- Change format and Mipmaps show the texture now and after side by side, with the memory each takes. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))
- Change format has no resampling choice: it uses the filter last applied in Resize or Mipmaps. ([#150](https://github.com/Hancapo/HikariHopper/pull/150))
- Renaming a texture says what is wrong with a name as you type it. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))
- Removing textures names them and shows what the dictionary holds now and after. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))
- Pasting an image shows its size, and a switch between replacing the selected texture and adding a new one. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))
- Importing images whose names clash lists which are new and which replace a texture. ([#151](https://github.com/Hancapo/HikariHopper/pull/151))


- Text fields in the texture editor's dialogs are as tall as the dropdowns beside them, and line up from one dialog to the next. ([#145](https://github.com/Hancapo/HikariHopper/pull/145))
- The texture editor says "1 mip", not "1 mips", and shows no mip slider for a texture with only one. ([#145](https://github.com/Hancapo/HikariHopper/pull/145))
- A bright model's glow in the 3D viewer fades smoothly into the sky instead of in visible rings. ([#160](https://github.com/Hancapo/HikariHopper/pull/160))

- The 3D viewer's exposure no longer changes as the camera comes closer or goes further: it is set by the time of day, as the world would be lit then, not by how much of the view the model fills. ([#162](https://github.com/Hancapo/HikariHopper/pull/162))
- Models open in the 3D viewer with the game's textures straight away: the game's textures are indexed while the game loads, and saving a model or texture dictionary into an RPF no longer makes the next launch index them again, and an update that changes how textures are found indexes them again, so no model is left missing textures it now finds. ([#164](https://github.com/Hancapo/HikariHopper/pull/164))
- The 3D viewer's parts panel header reaches the panel's edge, where it stopped a few pixels short. ([#170](https://github.com/Hancapo/HikariHopper/pull/170))
- A right-click on a part in the 3D viewer's parts panel opens its menu at once, where it took a second right-click. ([#171](https://github.com/Hancapo/HikariHopper/pull/171))
- The parts panel's menu shows the lines between its groups, as the app's other menus do. ([#171](https://github.com/Hancapo/HikariHopper/pull/171))
- After a game's archives change, its textures are indexed again in about half the time, as only the archives that changed are read. ([#187](https://github.com/Hancapo/HikariHopper/pull/187))

### Fixed

- Saving into, renaming or deleting an RPF in the game folder no longer fails with "Access to the path is denied" while the game's textures are being indexed after it opens. ([#185](https://github.com/Hancapo/HikariHopper/pull/185))

### Removed

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

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Hancapo/HikariHopper/compare/v0.0.5...v0.1.0
[0.0.5]: https://github.com/Hancapo/HikariHopper/compare/v0.0.4...v0.0.5
[0.0.4]: https://github.com/Hancapo/HikariHopper/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
