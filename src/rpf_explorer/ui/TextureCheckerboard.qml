import QtQuick
import "theme" as Theme

pragma ComponentBehavior: Bound

Item {
    id: checkerboard

    property int cellSize: 24
    readonly property int columnCount: Math.max(1, Math.ceil(width / cellSize))
    readonly property int rowCount: Math.max(1, Math.ceil(height / cellSize))

    clip: true

    Rectangle {
        anchors.fill: parent
        color: Theme.Theme.appBg
    }

    Repeater {
        model: checkerboard.columnCount * checkerboard.rowCount
        delegate: Rectangle {
            required property int index
            x: (index % checkerboard.columnCount) * checkerboard.cellSize
            y: Math.floor(index / checkerboard.columnCount) * checkerboard.cellSize
            width: checkerboard.cellSize
            height: checkerboard.cellSize
            color: (index + Math.floor(index / checkerboard.columnCount)) % 2
                ? Theme.Theme.appBg
                : Theme.Theme.chromeBg
        }
    }
}
