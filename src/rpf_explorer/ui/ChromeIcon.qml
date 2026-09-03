import QtQuick
import "theme" as Theme

/*
 * Chrome icons.
 *
 * These used to be drawn by hand, one case at a time, and looked it: no shared
 * grid, five chevrons at five different angles. They are Lucide now. The `kind`
 * vocabulary is kept so every call site is unchanged; this file is just the
 * translation from that vocabulary to a Lucide name.
 *
 * Add a kind by adding a line below and vendoring the icon into icons.js.
 */
Item {
    id: icon

    property string kind: ""
    property color stroke: Theme.Theme.textDim
    property real thickness: 1.8

    readonly property var names: ({
        "back": "chevron-left",
        "forward": "chevron-right",
        "chevron": "chevron-right",
        "caretUp": "chevron-up",
        "caretDown": "chevron-down",
        "up": "arrow-up",
        "refresh": "rotate-cw",
        "list": "list",
        "grid": "layout-grid",
        "checkerboard": "grid-3x3",
        "check": "check",
        "panel": "panel-right",
        "close": "x",
        "plus": "plus",
        "minus": "minus",
        "search": "search",
        "package": "package"
    })

    implicitWidth: 16
    implicitHeight: 16
    Accessible.ignored: true

    LucideIcon {
        anchors.fill: parent
        name: icon.names[icon.kind] !== undefined ? icon.names[icon.kind] : ""
        stroke: icon.stroke
        weight: icon.thickness
    }
}
