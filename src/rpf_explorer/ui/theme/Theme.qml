pragma Singleton
import QtQuick

QtObject {
    // Palette. The four base colours come from the reference character's
    // uniform: navy coat, blue piping, white gloves, pale green hair. Brass is
    // the fifth, taken from the cap badge and the coat buttons.
    //   #2C2D43 navy   #FFFBFF white   #4C7ECB blue   #D1F0AA green   #D0A94C brass
    // Every grey below is derived from the navy's hue; none of them is neutral.

    // Surfaces, darkest to lightest.
    readonly property color windowChrome: "#1B1C2C"   // hard seams and sunken wells
    readonly property color appBg: "#1E1F30"          // the list canvas
    readonly property color panelBg: "#1E1F30"
    readonly property color tabActive: "#1E1F30"      // active tab merges into the canvas
    readonly property color chromeBg: "#2C2D43"       // menu, toolbars, headers, status
    readonly property color sidebarBg: "#2C2D43"
    readonly property color navigationBg: "#2C2D43"
    readonly property color headerBg: "#2C2D43"
    readonly property color insetBg: "#1B1C2C"        // sunken fields
    readonly property color chromeRaised: "#33354C"   // menus, inactive tabs, raised buttons
    readonly property color tabBg: "#33354C"
    readonly property color hoverBg: "#2F3048"        // hover inside the list
    readonly property color hoverChrome: "#3A3C55"    // hover on chrome buttons

    // Lines. borderHard is the dark seam every panel sits behind, border is the
    // visible rule, guide is the dotted tree guide.
    readonly property color borderHard: "#1B1C2C"
    readonly property color border: "#43455F"
    readonly property color borderSoft: "#33354C"
    readonly property color guide: "#4A4C68"
    readonly property color bevel: Qt.rgba(1, 0.984, 1, 0.07)   // 1px highlight on a top edge

    // Ink.
    readonly property color text: "#FFFBFF"
    readonly property color textRow: "#C6C8DA"        // unselected row text
    readonly property color textDim: "#9A9CB4"
    readonly property color textFaint: "#6E7089"

    // Blue: what a package is, plus the piping on the window frame and the
    // active tab. Deliberately never the selection.
    readonly property color accent: "#4C7ECB"
    readonly property color borderAccent: "#4C7ECB"
    readonly property color accentWash: Qt.rgba(0.298, 0.494, 0.796, 0.30)
    readonly property color accentMuted: Qt.rgba(0.298, 0.494, 0.796, 0.55)

    // Green: what is selected, and the action on it.
    readonly property color selection: "#D1F0AA"
    readonly property color selectionText: "#1B1C2C"
    readonly property color selectionInk: Qt.rgba(0.106, 0.110, 0.173, 0.74)
    readonly property color selectionFaint: Qt.rgba(0.106, 0.110, 0.173, 0.58)
    readonly property color selectionWash: Qt.rgba(0.819, 0.941, 0.667, 0.13)  // unfocused pane
    readonly property color selectionRing: Qt.rgba(0.819, 0.941, 0.667, 0.55)

    // Neutral marquee: it is the selection instrument, not selected state.
    // Green appears only on rows that the marquee has actually included.
    readonly property color marqueeFill: Qt.rgba(0.173, 0.176, 0.263, 0.24)
    readonly property color marqueeBorder: Qt.rgba(0.776, 0.784, 0.855, 0.82)
    readonly property color marqueeHatch: Qt.rgba(0.604, 0.612, 0.706, 0.42)

    // Type inks — the identity of a FILE TYPE, carried on the icon only.
    //
    // A separate channel from the state colours above: row fills and text keep
    // their meanings, and an icon's tint says what a thing is. Blue already set
    // that precedent by marking packages, so these extend it rather than
    // contradict it.
    //
    // Every one sits at least 75 degrees from the accent blue in hue, because
    // blue is the only state colour ever painted on an icon and it must stay
    // unmistakable. Proximity to green or brass does not matter: green lives in
    // a row fill and brass in search text, never on a mark. All clear 4.8:1 on
    // both the list canvas and the sidebar.
    readonly property color inkCode: "#C98C79"    // exe, dll, asi   — hue 14
    readonly property color inkAsset: "#8FB29B"   // textures, models, audio, maps — hue 141
    readonly property color inkData: "#B88FB6"    // metadata, xml, text — hue 303

    // Brass: the app emblem, and search matches.
    readonly property color brass: "#D0A94C"

    // Kept for archive-parsing failures surfaced in the status bar. Outside the
    // character palette on purpose — nothing else in the UI should be red.
    readonly property color error: "#D9706C"

    readonly property string uiFont: "Archivo"
    readonly property string monoFont: "Space Mono"

    readonly property int windowTitleHeight: 30
    readonly property int menuHeight: 26
    readonly property int menuItemHeight: 25
    readonly property int menuSeparatorHeight: 9
    readonly property int menuMinimumWidth: 230
    readonly property int tabHeight: 28
    readonly property int navigationHeight: 40
    readonly property int headerHeight: 24
    readonly property int statusHeight: 24
    readonly property int statusHintInset: 10
    readonly property int splitHandleHitWidth: 8
    readonly property int rowHeight: 26
    readonly property int scrollbarWidth: 12
    readonly property int scrollbarIdleThumbWidth: 4
    readonly property int scrollbarActiveThumbWidth: 8
    readonly property int scrollbarMinThumbLength: 28
    readonly property int fontSize: 12
    readonly property int smallFontSize: 10

    // Texture dictionary viewer metrics. Its rows are taller only because the
    // thumbnail is the primary recognition aid rather than an entry glyph.
    readonly property int textureContextHeight: 42
    readonly property int textureRailWidth: 340
    readonly property int textureRowHeight: 62
    readonly property int textureThumbnailSize: 50
    readonly property int textureToolbarHeight: 38
    readonly property int textureMipBarHeight: 32
    readonly property int textureFactsHeight: 58

    // Fixed identity-window metrics. The splash is an expressive surface, but
    // its geometry remains part of the shared design contract.
    readonly property int splashWidth: 720
    readonly property int splashHeight: 400
    readonly property int splashTitleSize: 36
}
