import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

ScrollBar {
    id: control

    property string accessibleName: ""

    orientation: Qt.Vertical
    policy: ScrollBar.AlwaysOn
    hoverEnabled: true
    enabled: control.size < 1.0
    minimumSize: control.availableHeight > 0
        ? Math.min(1, Theme.Theme.scrollbarMinThumbLength / control.availableHeight)
        : 1
    implicitWidth: Theme.Theme.scrollbarWidth
    padding: 0
    Accessible.name: control.accessibleName

    background: Item {
        Rectangle {
            anchors.fill: parent
            visible: control.enabled && (control.hovered || control.pressed)
            color: Theme.Theme.insetBg

            Rectangle {
                anchors.left: parent.left
                anchors.top: parent.top
                anchors.bottom: parent.bottom
                width: 1
                color: Theme.Theme.borderHard
            }
        }
    }

    contentItem: Item {
        visible: control.enabled
        implicitWidth: Theme.Theme.scrollbarWidth

        Rectangle {
            anchors.right: parent.right
            anchors.rightMargin: 2
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            width: control.hovered || control.pressed
                ? Theme.Theme.scrollbarActiveThumbWidth
                : Theme.Theme.scrollbarIdleThumbWidth
            color: control.pressed
                ? Theme.Theme.textRow
                : (control.hovered ? Theme.Theme.textDim : Theme.Theme.guide)
        }
    }
}
