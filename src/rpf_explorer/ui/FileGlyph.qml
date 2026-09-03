import QtQuick
import "theme" as Theme

/*
 * Row-level marks for entries.
 *
 * Lucide geometry, tinted by family. The tint is the app's type-ink ramp: it
 * says WHAT a thing is, and it is the only channel that carries type. Row fills
 * and text keep their state meanings untouched (DESIGN.md 3).
 *
 * Two rules hold it together:
 *
 *   Blue stays the container colour. The type inks are all at least 75 degrees
 *   away from it in hue, because blue is the one accent that is ever painted on
 *   an icon and it must never be ambiguous.
 *
 *   A selected row overrides every tint with dark ink. A pale type colour on the
 *   green selection bar would be unreadable, and on that row the state matters
 *   more than the type.
 */
Item {
    id: glyph

    // Structural kind from the caller: folder | root | archive | package | file
    property string kind: "file"
    // The backend's kind string, which refines a plain file into its real type.
    property string fileKind: ""
    property bool selected: false

    implicitWidth: 16
    implicitHeight: 16
    Accessible.ignored: true

    readonly property bool isFolder: glyph.kind === "folder" || glyph.kind === "root"
    readonly property bool isPackage: glyph.kind === "archive" || glyph.kind === "package"

    // Backend kind -> [Lucide name, family]
    readonly property var byFileKind: ({
        "RPF Package":           ["package",         "container"],
        "Application":           ["app-window",      "code"],
        "Application Extension": ["puzzle",          "code"],
        "ASI Plugin":            ["plug",            "code"],
        "Audio Container":       ["audio-waveform",  "asset"],
        "Texture Dictionary":    ["book-image",      "asset"],
        "Drawable":              ["box",             "asset"],
        "Drawable Dictionary":   ["boxes",           "asset"],
        "Fragment":              ["zap",             "asset"],
        "Map Data":              ["map",             "asset"],
        "Collision Bounds":      ["triangle-dashed", "asset"],
        "Archetype Definitions": ["library",         "asset"],
        "Map Manifest":          ["list-tree",       "data"],
        "Node Dictionary":       ["route",           "asset"],
        "Navigation Mesh":       ["grid-3x3",        "asset"],
        "Clip Dictionary":       ["clapperboard",    "asset"],
        "Expression Dictionary": ["scan-face",       "asset"],
        "Particle Dictionary":   ["sparkles",        "asset"],
        "Audio Metadata":        ["database",        "data"],
        "Text Table":            ["languages",       "data"],
        "Metadata Container":    ["file-cog",        "data"],
        "Texture Parent Data":   ["file-stack",      "data"],
        "Height Map":            ["mountain",        "asset"],
        "Water Data":            ["droplets",        "asset"],
        "Cutscene":              ["film",            "asset"],
        "Vehicle Definitions":   ["car-front",       "data"],
        "Vehicle Handling":      ["wrench",          "data"],
        "Vehicle Colors":        ["palette",         "data"],
        "Vehicle Mod Colors":    ["palette",         "data"],
        "Vehicle Variations":    ["shuffle",         "data"],
        "Vehicle Layouts":       ["layout-template", "data"],
        "Ped Definitions":       ["users",           "data"],
        "Metadata":              ["file-cog",        "data"],
        "XML Document":          ["file-code",       "data"],
        "JSON Document":         ["file-code",       "data"],
        "Text Document":         ["file-text",       "data"],
        "Data File":             ["file",            "data"],
        "File":                  ["file",            "data"]
    })

    readonly property var resolved: {
        if (glyph.isFolder) return ["folder", "folder"];
        if (glyph.isPackage) return ["package", "container"];
        const hit = glyph.byFileKind[glyph.fileKind];
        return hit !== undefined ? hit : ["file", "data"];
    }

    readonly property color tint: {
        if (glyph.selected) return Theme.Theme.selectionText;
        switch (glyph.resolved[1]) {
        case "container": return Theme.Theme.accent;
        case "code": return Theme.Theme.inkCode;
        case "asset": return Theme.Theme.inkAsset;
        case "data": return Theme.Theme.inkData;
        }
        return Theme.Theme.textDim;
    }

    LucideIcon {
        anchors.fill: parent
        name: glyph.resolved[0]
        stroke: glyph.tint
    }
}
