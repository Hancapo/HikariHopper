import QtQuick
import "theme" as Theme

pragma ComponentBehavior: Bound

/*
 * A 1px dotted rule, built from discrete dots so it stays pixel-crisp at any
 * height. Used for the column separators and the folder-tree guides — the
 * classic tree-view hairline, which a solid border does not read as.
 */
Item {
    id: rule

    property color dotColor: Theme.Theme.guide
    property int dotSize: 1
    property int gap: 2
    property bool horizontal: false

    implicitWidth: horizontal ? 0 : dotSize
    implicitHeight: horizontal ? dotSize : 0
    width: horizontal ? undefined : dotSize
    height: horizontal ? dotSize : undefined
    clip: true

    Repeater {
        model: Math.max(0, Math.ceil((rule.horizontal ? rule.width : rule.height) / (rule.dotSize + rule.gap)))
        delegate: Rectangle {
            required property int index
            x: rule.horizontal ? index * (rule.dotSize + rule.gap) : 0
            y: rule.horizontal ? 0 : index * (rule.dotSize + rule.gap)
            width: rule.dotSize
            height: rule.dotSize
            color: rule.dotColor
        }
    }
}
