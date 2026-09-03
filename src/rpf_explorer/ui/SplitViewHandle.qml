import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

Item {
    id: handle

    implicitWidth: 1
    Accessible.name: qsTr("Resize panels")

    containmentMask: Item {
        x: (handle.width - width) / 2
        width: Theme.Theme.splitHandleHitWidth
        height: handle.height
    }

    Rectangle {
        anchors.fill: parent
        color: SplitHandle.pressed
            ? Theme.Theme.textRow
            : SplitHandle.hovered
                ? Theme.Theme.textDim
                : Theme.Theme.border
    }
}
