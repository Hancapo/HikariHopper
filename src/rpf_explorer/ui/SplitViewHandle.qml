import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

Item {
    id: handle

    implicitWidth: Theme.Theme.splitHandleHitWidth
    Accessible.name: qsTr("Resize panels")

    Rectangle {
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        anchors.horizontalCenter: parent.horizontalCenter
        width: 1
        color: SplitHandle.pressed
            ? Theme.Theme.textRow
            : SplitHandle.hovered
                ? Theme.Theme.textDim
                : Theme.Theme.border
    }
}
