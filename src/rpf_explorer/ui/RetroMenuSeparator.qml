import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

MenuSeparator {
    id: control

    implicitWidth: Theme.Theme.menuMinimumWidth
    implicitHeight: Theme.Theme.menuSeparatorHeight
    leftPadding: 11
    rightPadding: 11
    topPadding: 0
    bottomPadding: 0

    contentItem: Item {
        implicitWidth: Theme.Theme.menuMinimumWidth - control.leftPadding - control.rightPadding
        implicitHeight: control.implicitHeight

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            y: 3
            height: 1
            color: Theme.Theme.borderHard
        }

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            y: 4
            height: 1
            color: Theme.Theme.bevel
        }
    }
}
