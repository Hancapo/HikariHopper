# HikariHopper — Design Language

This document is the contract for every pixel in this application. If you are
adding a window, a dialog, a panel, a control, or a single label, read the
relevant section first and follow it exactly. Where this document and your
instinct disagree, this document wins.

It is written to be followed literally, including by an AI assistant working on
this repo. Nothing here is aspirational: every rule describes what the code in
`src/rpf_explorer/ui/` already does, and every token name is real.

---

## 1. The one sentence

**This is a desktop tool with a light retro accent, not a web app in a window.**

Everything below is a consequence of that sentence. When a decision is unclear,
ask: *would a native Windows file manager from a careful developer do this?* If
the honest answer is "no, but a web dashboard would" — do the other thing.

---

## 2. Where the look comes from

The palette is lifted from a character reference: a military/police style
uniform in navy with blue piping, white gloves, pale green hair, and brass
hardware on the cap badge and coat buttons.

| Source on the uniform | Colour | Role in the UI |
|---|---|---|
| The coat, cap, skirt, boots | `#2C2D43` | Every surface |
| Gloves and tights | `#FFFBFF` | Text |
| The piping along collar, cuffs, hems | `#4C7ECB` | Packages, and the window/tab trim |
| Hair and halo | `#D1F0AA` | Selection, and the action on it |
| Cap badge and buttons | `#D0A94C` | App emblem, and search matches |

Two things follow from this that are easy to get wrong:

- **Blue is a piping colour.** On the uniform it is a thin line along an edge,
  never a fill. In the UI it is a 1px border on the window frame and the active
  tab, plus the package identity. It is *not* a background wash and it is *not*
  the selection.
- **Green is not on the uniform at all.** It is the character. That is why it
  carries the one thing that is about *you* rather than about the data: what you
  have selected and what you can do to it.

### And rails

The palette is only half of it. The character and her twin sister are named
after **Shinkansen services** — Hikari and Nozomi, which together with Kodama
are the three service classes on Japan's Tokaido line. Hikari is the middle one:
faster than the all-stops train, slower than the express. The app's own name
carries it further, since a hopper is a freight wagon as well as something that
hops between archives.

So rail is not a decoration bolted on afterwards; it is the other half of where
the look comes from, and several rules that already exist are better explained
by it than by the uniform:

| Rail source | Already in the UI |
|---|---|
| Livery striping down the flank of a train | The diagonal strips on the splash |
| Track: two rails and evenly pitched sleepers | `DottedRule` — the tree guides and column separators |
| A colour-light signal showing green for *proceed* | Green as the action and the selection |
| Platform departure boards | Mono uppercase headers, and the key-then-action pairs in the status bar |
| A ticket stub, or a rolling-stock number plate | Small letterspaced mono on plates and tags |

**What this authorises:** geometry and typographic register. Diagonal livery
strips, parallel rules at an even pitch, the clipped mono voice of a departure
board, plates and tags that read like rolling-stock markings.

One more thing the reference settles: **the character is soft-spoken and
reserved**, and does not express herself loudly. That is a tone anchor, not
trivia — it argues for the quiet end of every choice where this document leaves
room, the splash included. Few strips, thin ones, a lot of navy.

**What it does not authorise:** literal imagery. No locomotive glyphs, no rail
clip-art, no station-jingle sounds, no track running decoratively across a
working panel. The rail reading earns its place where it explains a shape the
tool needed anyway — never as a picture of a train.

**Boundary.** Taking a palette and a material vocabulary from a character is
fine. Reproducing that game's own interface, its branding, its logo, or the
character herself is not, and the halo must never become this app's mark. What
we borrow is colour and the logic of how a uniform uses trim.

---

## 3. Colour: one job per colour

This is the most important rule in the document, and the one most likely to be
broken by a well-meaning addition.

**It binds working surfaces.** Colour carries state only where there is state to
carry: the main window, its panels, its lists, any dialog you open mid-task. A
surface with no live data and nothing selectable — a splash screen, an app icon,
an identity block — is not bound by the table below. See section 17; that is a
real allowance, not a loophole.

| Colour | Its single job | Never |
|---|---|---|
| **Blue** `#4C7ECB` | "This is a package/archive" + window and active-tab trim | Selection, general emphasis, a fill behind text |
| **Green** `#D1F0AA` | "This is selected" + the primary action on the selection — a signal showing *proceed* | Success messages, decoration, a second accent |
| **Brass** `#D0A94C` | The app emblem, and characters matched by the current search | Warnings, sort order, badges, "new", "pinned" |
| **Red** `#D9706C` | Archive parsing failures surfaced to the user | Anything else — nothing else in this UI is red |

**Never offer an action the app cannot perform.** If a control lists something
unsupported — a game with no keys, a view not built yet — show it disabled with
a short reason next to it, the way `StartPage` lists the Rockstar titles
FiveFury has no keys for. A greyed row with an explanation is honest; an
enabled one that fails is not.

### Type inks: a second channel, on icons only

The table above governs **state**. There is one more channel, and only one:
**an entry icon's tint says what kind of thing it is.**

| Ink | Family |
|---|---|
| `accent` blue | Containers — folders' packages, the RPF itself |
| `inkCode` `#C98C79` | `.exe`, `.dll`, `.asi` — anything that executes or injects |
| `inkAsset` `#8FB29B` | Textures, drawables, fragments, audio, map data |
| `inkData` `#B88FB6` | Metadata, XML, text, unknown |

This does not contradict the one-job rule; blue already set the precedent by
marking packages on the icon, where it means *identity*, not *state*. Three
constraints keep the two channels apart:

- **Only the icon is tinted.** Row fills and text ink never carry type.
- **Every type ink is at least 75 degrees from the accent blue in hue**, because
  blue is the one state colour ever painted on an icon. Proximity to green or
  brass does not matter — green lives in a row fill, brass in search text, and
  neither is ever a mark.
- **Selection wins.** A selected row overrides every tint with `selectionText`.
  A pale type colour on the green bar would be unreadable, and there the state
  matters more than the type.

If you have a new state to express and none of these fit, **the answer is not a
new colour.** Use weight, position, a hairline, an icon, or plain words. A fifth
accent will break the system.

There is exactly one deliberate double-use: brass is both the static app emblem
and the transient search highlight. That works because the emblem never changes
and the highlight only exists while a query is active — they never read as the
same signal.

### Selection is green; location is green too, but quieter

Two panes, two weights, same colour. This is how a native app shows which pane
has keyboard focus:

- The pane **with focus** (the entry table): selection is a **solid green bar,
  full bleed across the data strip, with dark ink on top** (`selection` /
  `selectionText`). It ends after `SIZE`; trailing canvas is not part of a row.
- The pane **without focus** (the folder tree): where-you-are is a **green wash
  with a 1px green ring** (`selectionWash` / `selectionRing`), text stays light.

Never give both panes the solid bar. Never use blue for either.

### Token reference

Use tokens. Never write a hex literal in a component — if you need a colour that
does not exist, add it to `theme/Theme.qml` with a comment explaining its job.

**Surfaces** (darkest to lightest)

| Token | Value | Use |
|---|---|---|
| `windowChrome` / `borderHard` / `insetBg` | `#1B1C2C` | Hard seams, sunken wells |
| `appBg` / `panelBg` / `tabActive` | `#1E1F30` | The list canvas; the active tab merges into it |
| `chromeBg` / `sidebarBg` / `navigationBg` / `headerBg` | `#2C2D43` | Menu bar, toolbars, panel headers, status bar |
| `chromeRaised` / `tabBg` | `#33354C` | Popup menus, inactive tabs, raised buttons |
| `hoverBg` | `#2F3048` | Hover **inside the list** |
| `hoverChrome` | `#3A3C55` | Hover **on chrome** (buttons, menu titles, tree rows) |

**Lines**

| Token | Value | Use |
|---|---|---|
| `borderHard` | `#1B1C2C` | The dark seam every panel sits behind |
| `border` | `#43455F` | Visible rules and control borders |
| `borderSoft` | `#33354C` | Pressed-state fills |
| `guide` | `#4A4C68` | Dotted tree guides and column separators |
| `bevel` | white @ 7% | The 1px light edge along a top border |
| `borderAccent` | `#4C7ECB` | Window frame and active tab only |

**Ink**

| Token | Value | Use |
|---|---|---|
| `text` | `#FFFBFF` | Primary text, selected tree label |
| `textRow` | `#C6C8DA` | Default row text — this is the list's resting colour, not `text` |
| `textDim` | `#9A9CB4` | Column headers, secondary column values |
| `textFaint` | `#6E7089` | Placeholders, hint labels, disabled |

**On the green bar** — never use light ink on green:

| Token | Use |
|---|---|
| `selectionText` | Primary text and the size column |
| `selectionInk` | Secondary values (the type column) |
| `selectionFaint` | Tertiary values (item counts) |

---

## 4. Typography

Two families, both bundled under the OFL in `ui/fonts/` and registered in
`app.py`. Never add a third, and never rely on a system font.

**Archivo** (`Theme.uiFont`) — the interface voice. Menu titles, button labels,
type names, prose, dialogs. It is a plain utilitarian grotesque chosen because
it does *not* have the flavour of a web product font.

**Space Mono** (`Theme.monoFont`) — every piece of *data*, and the entire retro
register. File names, sizes, paths, counts, key names, column headers, status
bar, tab labels.

**This split is the rule that carries the retro feeling.** Do not reach for
bevels or pixel art to make something feel retro — put the data in Space Mono
and it will. Conversely: never set body prose or a button label in mono.

Sizes come from `Theme.fontSize` (12) and `Theme.smallFontSize` (10). Column
headers and small caps labels are mono, bold, 10px or below, with
`font.letterSpacing` between 0.9 and 1.2.

> **Gotcha:** `font.pixelSize` is an **integer** in Qt. Writing `11.5` fails at
> load with `Invalid property assignment: int expected`, and the whole component
> tree becomes unavailable. This has already broken the build once.

---

## 5. Metrics and density

This is a dense desktop tool. Rows are 26px, not 44px. That is deliberate: at
touch-target sizing the tool stops being able to show a directory.

| Token | Value |
|---|---|
| `windowTitleHeight` | 30 |
| `menuHeight` | 26 |
| `tabHeight` | 28 |
| `navigationHeight` | 40 |
| `headerHeight` | 24 |
| `statusHeight` | 24 |
| `rowHeight` | 26 |
| `gridCellWidth` / `gridCellHeight` | 148 / 112 |
| `gridGlyphSize` | 34 |
| `gridCellInset` / `gridContentInset` | 3 / 10 |

Use the tokens; do not hardcode heights. If a new bar needs a height, add a
token for it.

**Radii are 0.** There is no `radius:` anywhere in this UI and there should
never be one. Application-authored corners are square. HikariHopper windows
are frameless and paint their own square blue piping; platform-owned file and
folder dialogs are the exception and retain their native decoration.

---

## 6. Surfaces, seams and bevels — the 1px system

Depth in this app is expressed with single-pixel lines, never with shadows,
blurs, or gradients. The dotted ones are track: parallel, evenly pitched, and
read as sleepers rather than as a broken line (section 2).

**A raised edge** — a light 1px line along the top:

```qml
Rectangle { anchors.left: parent.left; anchors.right: parent.right
            anchors.top: parent.top; height: 1; color: Theme.Theme.bevel }
```

**A hard seam** — a dark 1px line where a panel ends:

```qml
Rectangle { anchors.left: parent.left; anchors.right: parent.right
            anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }
```

**A sunken well** — fields and pressed segments:

```qml
color: Theme.Theme.insetBg
border.width: 1
border.color: Theme.Theme.borderHard
```

**A dotted hairline** — column separators and tree guides. Use the `DottedRule`
component; do not fake it with a low-alpha solid line, which reads as a smudge
rather than a hairline:

```qml
DottedRule { anchors.right: parent.right; anchors.top: parent.top
             anchors.bottom: parent.bottom }
```

Banned outright: `layer.effect` drop shadows, `Gradient`, `opacity` used to fake
depth, and any blur.

---

## 7. Icons: Lucide, drawn as paths

**Never put a symbol character in a `Text` and call it an icon.** Not `↻`, not
`☰`, not `▦`, not `×`, not emoji. The bundled fonts have no glyphs for these and
they render as empty boxes — this was a real, shipped bug.

The set is **[Lucide](https://lucide.dev)**, ISC licensed, vendored into
`ui/icons.js` with the licence beside it in `ui/LICENSE-Lucide.txt`. An earlier
pass drew these by hand and they looked it; Lucide gives a set built to one grid
by people who do this full time.

**Why paths and not SVG images.** Qt does not resolve Lucide's
`stroke="currentColor"`, and this UI recolours icons at runtime — every mark
darkens when its row is selected. So the geometry is stored as path data and
stroked by `LucideIcon`, which keeps runtime colour, scales cleanly, and needs
no SVG plugin or graphical-effects dependency.

| Component | Use |
|---|---|
| `LucideIcon` | One mark. `name` is the Lucide icon name, plus `stroke` and `weight` |
| `ChromeIcon` | Chrome marks by the app's own `kind` vocabulary, which it maps to Lucide names |
| `FileGlyph` | Entry marks. Resolves the backend's kind string to an icon *and* a type ink |

**To add an icon:** vendor its path data into `ui/icons.js` from the Lucide
source, then add a line to the map in `ChromeIcon` or `FileGlyph`. Convert any
`<circle>`, `<rect>`, `<line>` or `<polyline>` to path data first, so every mark
is one uniform list of subpaths — and parse SVG attributes **by name**, never by
position: Lucide does not keep a fixed attribute order, and assuming one
silently drops half of several icons.

## 8. State and interaction

**Everything you can point at must react.** A control with no hover state feels
dead, and the usual cause is not a missing rule but a broken one — see the two
traps below.

| State | How it looks |
|---|---|
| Hover, in a list | `hoverBg` fill |
| Hover, on chrome | `hoverChrome` fill |
| Hover, on a sunken field | Well lightens, border rises to `border`, caret to `text` |
| Hover, on a fill-less mark | The glyph alone goes `textDim` → `text` |
| Pressed, filled | `borderSoft`; a primary button darkens its own green |
| Pressed, fill-less | The glyph steps back to `textRow`. **No fill appears** |
| Selected (focused pane) | Solid `selection`, dark ink |
| Current location (unfocused pane) | `selectionWash` + `selectionRing` |
| Active segment in a segmented control | **Sunken**, not tinted — `sunken: true` on `ChromeToolButton` |
| Disabled | `textFaint`, no hover response |

### Trap 1: never override `background` on a ChromeToolButton

The button computes hover and pressed from its variant. Replacing `background`
with your own `Rectangle` throws all of that away and leaves a control that
never reacts — this shipped once, on four of the most visible buttons in the
app. Pick a variant instead: `primary` for the green action, `raised` for a
chrome button, neither for a ghost, and `hoverFill: false` for a bare mark.

### Trap 2: `MouseArea.containsMouse` loses to controls on top

If a row has a button inside it — a tab with a close mark, a tree row with an
expand box — a `MouseArea` on the row stops reporting hover while the pointer is
over that child, so the row's highlight flickers off. Use a **`HoverHandler`**,
which is passive and does not compete for the grab. A `MouseArea` is still the
right thing for the click itself.

A selected mode in a group of buttons is shown by *recessing* it, the way a
native toolbar does. It is not shown with an accent colour — that would spend
green or blue on something that is neither a selection nor a package.

Motion: there is none in the chrome. No transitions, no easing curves, no
animated highlights — hover and press are instant. This is a tool.

The entry table follows native desktop multi-selection: a plain click replaces
the selection, Ctrl toggles a row, Shift extends from the last plain selection,
and Ctrl+A selects the visible rows. Dragging any selected row exports the whole
selection as copies; loose files retain their source paths, while archive entries
are materialized as standalone files through FiveFury before the native drag.
Dragging from empty table canvas creates a neutral, diagonally hatched marquee.
Plain marquee selection replaces the current set, Ctrl toggles against the set
captured at drag start, and Shift adds to it. It begins only after the native
drag threshold, so an empty-canvas click remains a quiet deselect action. The
row hit area and its hover/selection fill end after `SIZE`; the remaining width
is a neutral canvas, not an invisible continuation of every list item. Vertical
overlap alone never selects a row: the marquee must also cross into the data
strip, so its geometry and the resulting green rows remain causally connected.
The marquee uses navy, guide-grey hatching, and a pale neutral border: green is
reserved for the resulting selection and never paints the instrument itself.

The folder tree separates focus from activation. One click gives a row a neutral
focus treatment without navigating; double click activates both folders and RPF
packages. The explicit `+` / `−` box remains the direct expansion control, and
Enter activates the focused row just like double click.

Two things are not decoration and are allowed: an indicator that moves because
something real is moving (an indeterminate progress mark while an archive is
being read), and motion on an expressive surface (section 17).

---

## 9. Components — use these, do not re-roll

| Component | What it is for |
|---|---|
| `ChromeToolButton` | Every button in chrome. Variants: `primary`, `raised`, `sunken`, `hoverFill`. Also `iconKind`, `bordered`, `foreground` |
| `LucideIcon` | One Lucide mark, by name |
| `ChromeIcon` | Chrome marks by `kind`, mapped to Lucide |
| `FileGlyph` | Row-level file/folder/archive marks |
| `SelectionMarquee` | Neutral hatched rubber band for pointer box selection |
| `QuietScrollBar` | Shared vertical scrollbar: fixed 12px gutter, 4px idle thumb, 8px hover/pressed thumb |
| `FlatTextField` | Every text input. Supports `leadingIcon`, `leadingColor`, `keyCap` |
| `FlatComboBox` | Every dropdown. Model entries may set `supported: false` + `note` to appear disabled with a reason |
| `DottedRule` | Dotted separators and tree guides, horizontal or vertical |
| `RetroMenu` / `RetroMenuItem` / `RetroMenuSeparator` | Connected command-rail dropdowns, with right-aligned shortcut text |
| `MenuBarButton` | Compact content-width top-level menu title |
| `MenuRow` | The menu bar |
| `WindowTitleBar` | Shared application-owned title bar and window controls |
| `WindowControlButton` / `WindowControlGlyph` | 34px divided title controls with exact 1px geometry |
| `WindowResizeFrame` | Invisible edge grips delegated to the window manager |
| `TabStrip` | Document tabs |
| `NavigationBar` | Back/forward/up, address bar, search, view modes |
| `FolderPanel` | The headerless folder tree |
| `EntryTable` | The detail list |
| `EntryGrid` / `EntryGridCell` | The compact icon field and its fixed-size entry cells |
| `StatusBar` | The bottom bar |

Screens, one level up:

| Screen | What it is |
|---|---|
| `main.qml` | The frameless window. Owns the title bar and blue piping |
| `ExplorerScreen` | Menu bar + tabs + a `Loader` that swaps the body |
| `WorkspaceView` | The body when a workspace is open: nav bar, tree, table, status bar |
| `StartPage` | The body when the active tab has no workspace yet |

If you need something that does not exist, build it as a new component in
`ui/` rather than inlining it — and give it the same shape as its neighbours
(`required property` for injected data, tokens for every colour).

New `.qml` files under `ui/` are packaged automatically by the
`[tool.setuptools.package-data]` glob in `pyproject.toml`. New subdirectories
are **not** — add them to that list.

---

## 10. Window anatomy

The main window stacks fixed-height chrome around one flexible content area:

```
┌──────────────────────────────────────────────┐ ← 1px blue piping, drawn last, z:100
│ ◩ HikariHopper                      ─  □  ×  │  windowTitleHeight
│ menu bar                                     │  menuHeight
│ tabs                                         │  tabHeight
│ ‹ › ↑ │ address bar │ search │ view modes    │  navigationHeight
├───────────┬──────────────────────────────────┤
│  tree     │ NAME        TYPE          SIZE   │  headerHeight on the table only
│           │  rows                            │  fills
├───────────┴──────────────────────────────────┤
│ 14 items │ Selected … │ Ctrl+O Game folder   │  statusHeight
└──────────────────────────────────────────────┘
```

The body between the tab strip and the status bar is swapped by a `Loader` in
`ExplorerScreen`: `WorkspaceView` once a workspace is open, `StartPage` before
that. A new full-screen state belongs there as a third component, not as a
modal over an empty table.

Rules for any new window or dialog:

- Column-bearing panels get a **header strip** at `headerHeight` in
  `chromeRaised`, with a mono, bold, letterspaced, uppercase label, a `bevel`
  on top and a `borderHard` at the bottom. The folder tree is deliberately
  headerless: its hierarchy identifies the pane without spending a row on a
  redundant `FOLDERS` label.
- Panels are separated by a 1px `border` `SplitView` handle.
- List and grid are two presentations of the same entry model and selection.
  Grid cells are unboxed at rest, use the existing type-aware `FileGlyph`, and
  carry the same solid green selection as table rows. Arrow keys navigate by
  grid geometry; Ctrl/Shift selection, drag export, sorting, search marking,
  double-click activation and rectangular marquee remain available.
- The top-level menu begins 1px from the frame and each title takes its content
  width plus 12px horizontal padding. While a popup is visible, its title keeps
  the `chromeRaised` active surface and opens the bottom seam so title and popup
  read as one connected object.
- Popup rows use the command-rail treatment: `hoverChrome` plus a 2px
  `textRow` marker. Checks are conventional checkmarks in a stable left gutter;
  blue fills and decorative diamonds are not menu states.
- Every HikariHopper-owned top-level window is frameless and uses the shared
  `WindowTitleBar`; do not mix Windows decoration with application chrome.
  Moving and resizing must still be delegated through `startSystemMove()` and
  `startSystemResize()` so snapping, constraints and multi-monitor behaviour
  remain native. Platform-owned file and folder dialogs keep their native frame.
- Title controls use 34px divided cells and application-drawn 1px marks. Hover
  brightens only the mark from `textDim` to `text`; it never fills the cell.
  Pressing offsets the mark down by 1px, while keyboard focus adds a short rule.
- A window that owns its own frame carries the blue piping. A panel inside a
  window does **not** — the piping appears once, at the outermost edge.
- The status bar is divided into cells by a 2px seam (`borderHard` with a
  `border` highlight on its right edge), not by whitespace.

---

## 11. Copy and voice

The register to aim for is a **departure board**: clipped, factual, no adjectives,
the key on the left and where it takes you on the right.

- **Sentence case** for menu items and buttons: `Open GTA V folder…`, not
  `Open GTA V Folder`.
- **UPPERCASE, mono, letterspaced** for panel and column headers only:
  `NAME`, `TYPE`, `SIZE`.
- **Ellipsis (`…`, one character)** on any command that opens a dialog.
- **Function keys, not web shortcuts.** `F3`, `F5`, `Backspace`, `Alt+←`.
  Never a `⌘K`-style command-palette hint.
- In the status bar, shortcut hints are **key in `textRow`, action in
  `textFaint`**, in that order: `Ctrl+O Game folder`.
- Every user-visible string goes through `qsTr()`.
- No exclamation marks, no emoji, no "Oops".

---

## 12. Accessibility floor

- Every interactive element that is not self-describing gets an
  `Accessible.name` with a `qsTr()` string.
- Icon-only buttons **always** need one.
- Anything clickable must also be reachable and operable from the keyboard —
  see the tree's expand box, which handles `Keys.onSpacePressed` and
  `Keys.onReturnPressed` alongside its `MouseArea`.
- Decorative marks set `Accessible.ignored: true` (see `FileGlyph`).
- Density is deliberately tight, but never put an interactive target below
  16×16px, and give it a `MouseArea` at least as large as its visual.

---

## 13. Anti-patterns: what "web smell" means here

Each of these was found in this codebase and removed. If you catch yourself
writing one, the substitute is in the right column.

| Do not | Instead |
|---|---|
| Rounded corners (`radius: 6`) | Square. `radius` is 0 everywhere |
| Pill-shaped chips / tags | Square 1px-bordered labels, mono, small caps |
| Floating list rows with gaps and rounded highlights | Full-bleed rows, no gap, no radius |
| Soft drop shadows, blurs, gradients | 1px bevels and hard seams |
| A big filled rounded primary button | A flat, square toolbar button |
| `⌘K` / `Ctrl+K` command-palette hints | A `F3` key cap inside the field |
| Symbol characters as icons | `ChromeIcon` / `FileGlyph` |
| Animated hovers and transitions | Instant state change |
| Accent-coloured "active" states in a button group | A sunken segment |
| Cool blue-white text (`#f0f1ff`) | `#FFFBFF`, the warm white from the palette |
| Neutral greys | Greys derived from the navy's hue — every one in `Theme.qml` is |

---

## 14. Deliberately rejected — do not re-propose

These were considered, built, or shipped in the explorer's working surfaces, and
then cut for reasons that still hold. Re-adding any of them needs a real argument, not an aesthetic impulse.

- **Pinned / favourite items.** Cut outright — not a feature this tool will
  have. This is also why brass is *not* a "pinned" marker.
- **An archive block map** (a defragmenter-style layout strip). Handsome, and
  far too technical for a browser.
- **Compression ratio, packed vs. on-disk sizes, byte offsets, CRC32, mipmap
  counts.** Nobody looking for a file reads these. One `SIZE` column is the
  whole budget.
- **"Mount", "Unmount", "Repack", "Verify CRC"** as primary actions. That is
  packer vocabulary. This is an explorer: Open, Go, Search, Extract a copy.
- **A per-row "packed / on disk" badge.** Inside an archive everything is
  packed; saying it on every line is noise. The address bar says it once, in the
  blue package segment.
- **The word "Inspector"** for the preview surface. An inspector audits a file;
  here you are trying to *recognise* one. If a preview pane returns, it is a
  preview: the thing at a good size, four facts in plain language, and the
  obvious action.

---

## 15. Adding a new window — the procedure

1. **Compose from existing components.** A new window almost always means a
   menu bar, some chrome bars, column headers where needed, and a status bar.
2. **Take colours from tokens only.** No hex literals in components.
3. **Data in Space Mono, interface in Archivo.**
4. **Draw every icon.** Add kinds to `ChromeIcon` as needed.
5. **Give each new colour meaning a job** — or, far more likely, discover it
   already has one.
6. **Wire it to the right object.** Each tab owns its own `ExplorerBridge`;
   `ExplorerTabs` (`tabs.py`) is the list of them. Chrome that spans tabs takes
   `required property var tabs` and reads `tabs.activeBridge`; anything inside
   one workspace takes `required property var bridge` and is handed the active
   one. Add `@Property` / `@Slot` members to `ExplorerBridge` rather than
   reaching into models from QML, and remember every `@Property` needs a
   `notify` signal or the UI will not update.
7. **Add `Accessible.name` as you go**, not afterwards.
8. **Render it and look at it** before saying it is done.

### Checklist

- [ ] No `radius:` anywhere
- [ ] No hex literals outside `Theme.qml`
- [ ] No symbol characters used as icons
- [ ] No `font.pixelSize` with a decimal point
- [ ] Data is mono, interface is Archivo
- [ ] Selection is green; blue is only packages and trim; brass is only emblem and search
- [ ] Column headers: `chromeRaised`, mono, bold, uppercase, letterspaced, bevel on top, hard seam below
- [ ] Every icon-only control has an `Accessible.name`
- [ ] Every pointable control reacts to hover, and to press
- [ ] No `background:` override on a `ChromeToolButton`
- [ ] Row hover inside a row that contains a button uses `HoverHandler`
- [ ] Keyboard reaches everything the mouse can do
- [ ] Shortcut hints use function keys, in `textRow` + `textFaint` pairs
- [ ] `python -m pytest -q` passes
- [ ] `python -m ruff check .` passes
- [ ] The QML loads with **no** warnings you introduced
- [ ] Any exception you took is commented at the site and listed in section 17

---

## 16. Verifying your work

Tests and lint:

```bash
python -m pytest -q
python -m ruff check .
```

QML errors are silent by default — `engine.rootObjects()` simply comes back
empty and the app exits with a generic message. Always connect the warning
signal when debugging:

```python
engine.warnings.connect(lambda errors: [print("QML:", e.toString()) for e in errors])
```

To see the interface without a GTA V installation, render it offscreen with a
stub provider and fake entries: set `QT_QPA_PLATFORM=offscreen` and
`QT_QUICK_BACKEND=software`, swap `bridge.provider` for a stub, populate
`bridge.entriesModel` / `bridge.treeModel`, then grab the root
`QQuickItem` with `grabToImage()`. Note that `grabWindow()` does **not** work
here: the QML root arrives as a plain `QWindow` wrapper.

When vendoring icon geometry, render every icon you added and look at it. A
mis-parsed `<rect>` produces a mark that is *incomplete*, not one that errors —
several icons shipped as half-drawn before this was caught by eye.

Known pre-existing noise, not caused by your change: two
`Binding loop detected for property "implicitWidth"` warnings from the `Dialog`
elements in `MenuRow.qml`.

---

## 17. Exceptions

A design system that only ever says no gets ignored, and then it stops
describing the product. This section is how an exception gets taken on purpose
instead of by erosion.

### Two kinds of surface

**Working surfaces** are everything present while someone is using the tool: the
main window and its chrome, panels, lists, the address bar, dialogs opened
mid-task. Here colour is a code — blue means package, green means selected,
brass means matched — and every rule in this document binds without argument.
Ambiguity here costs someone a misread file.

**Expressive surfaces** carry no live state and no data: a splash screen, the
app icon, the identity block in About, a first-run or installer screen, an
illustration in an empty state. Nothing on them is selectable, nothing is a
package, nothing is a search hit. The one-job-per-colour rule has nothing to
disambiguate, so it does not apply — the palette is a palette again, and the
design can be as expressive as it deserves to be.

This is the distinction the document was missing when it was first written, and
it is why a green splash screen is not a violation.

### The test

Before taking an exception, three questions. Any *yes* means no exception:

1. Does this surface show live state that the colour in question is already
   responsible for elsewhere?
2. Could someone reasonably read the colour as that state?
3. Does it sit inside the working window? A splash is its own window; a green
   banner across the entry table is not.

### What never bends

Expressive or not, these hold everywhere:

- **The palette.** Five colours and greys derived from the navy. No sixth hue,
  no stock blue, no neutral grey.
- **Square corners.** `radius` stays 0.
- **The two typefaces**, and the split between them: data in Space Mono,
  interface in Archivo.
- **Drawn marks.** No symbol characters as icons, no emoji.
- **Never lie.** No fake progress, no control that promises what the app cannot
  do (sections 11 and 14).
- **The accessibility floor** (section 12). Expressive does not mean unreadable.

### How to take one

1. Comment it at the site, saying which rule and why the test passes.
2. Add a row to the register below, so exceptions are countable rather than
   scattered.

### Register

| Surface | Rule set aside | Why the test passes |
|---|---|---|
| Entry icons | Section 3 — the palette was five colours | Type inks are a separate channel from state, on the icon only, and every one is kept 75 degrees clear of the accent blue. Written up as a refinement in section 3 rather than left as a loose exception. |
| Splash screen | Section 3 — green reserved for selection and its action | Its own window, shown before the explorer exists. Nothing on it is selectable, no packages, no search. The reference character's palette is the whole point of the surface. |

### Splash screen, specifically

Since it is the first exception on the register, the shape it should take:

- Green may dominate. This is the one surface where the reference the palette
  came from is allowed to show through as design rather than as a colour code.
- Diagonal strips are the house motif here, read as livery down the flank of a
  train (section 2) — not as generic decoration. Few of them, and thin: the
  strips are trim, the way the uniform uses its piping.
- Keep the material language: square, the two typefaces, drawn marks, the 1px
  system if anything is framed.
- **Do not fake progress.** A bar that is not measuring anything is a lie
  (section 11). If load time is not measured, show no bar.
- The bottom strip names the operation actually running on the left and its
  short phase (`BOOT`, `CORE`, `GAME`, `UI`, `WINDOW`) on the right. Startup
  work begins only after the splash has presented its first frame.
- Dismissible by click or any key, and never the thing standing between someone
  and their work.
- Hand off without a jump: sit the splash on one of the app's own surface
  tokens so the explorer does not appear to change palette when it opens.
