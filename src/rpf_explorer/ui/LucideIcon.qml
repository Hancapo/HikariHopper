import QtQuick
import QtQuick.Shapes
import "icons.js" as Lucide
import "theme" as Theme

pragma ComponentBehavior: Bound

/*
 * One Lucide mark, drawn as paths.
 *
 * The geometry is vendored in icons.js on Lucide's 24x24 grid and scaled here,
 * so an icon is crisp at any size and — the reason for drawing rather than
 * loading SVG images — can be recoloured at runtime. Qt does not resolve
 * Lucide's stroke="currentColor", and this UI has to darken every icon when its
 * row is selected.
 *
 * The stroke is scaled with the item so a 16px icon keeps Lucide's proportions
 * instead of turning into a blob.
 */
Item {
    id: icon

    property string name: ""
    property color stroke: Theme.Theme.textDim
    // Lucide draws at 2 units on a 24 grid. A hair under that reads better at
    // the sizes this app uses icons at.
    property real weight: 1.8

    implicitWidth: 16
    implicitHeight: 16
    Accessible.ignored: true

    readonly property real span: Math.min(width, height)
    readonly property var subpaths: Lucide.paths[icon.name] !== undefined
        ? Lucide.paths[icon.name]
        : []

    Repeater {
        model: icon.subpaths
        delegate: Shape {
            id: segment
            required property string modelData
            anchors.fill: parent
            preferredRendererType: Shape.CurveRenderer

            ShapePath {
                strokeColor: icon.stroke
                strokeWidth: icon.weight
                fillColor: "transparent"
                capStyle: ShapePath.RoundCap
                joinStyle: ShapePath.RoundJoin
                scale: Qt.size(icon.span / 24, icon.span / 24)
                PathSvg { path: segment.modelData }
            }
        }
    }
}
