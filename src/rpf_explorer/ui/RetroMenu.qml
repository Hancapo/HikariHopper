import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

Menu {
    id: control
    property int connectionWidth: 0

    delegate: RetroMenuItem { }

    implicitWidth: Math.max(
        Theme.Theme.menuMinimumWidth + leftPadding + rightPadding,
        implicitContentWidth + leftPadding + rightPadding
    )
    topPadding: 4
    bottomPadding: 4
    leftPadding: 1
    rightPadding: 1
    overlap: 1
    margins: 0

    background: Rectangle {
        color: Theme.Theme.chromeRaised

        Rectangle {
            anchors.left: parent.left
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            width: 1
            color: Theme.Theme.borderHard
        }
        Rectangle {
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            width: 1
            color: Theme.Theme.borderHard
        }
        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            color: Theme.Theme.borderHard
        }
        Rectangle {
            x: control.connectionWidth
            anchors.right: parent.right
            anchors.top: parent.top
            height: 1
            color: Theme.Theme.borderHard
        }
        Rectangle {
            x: control.connectionWidth + 1
            anchors.right: parent.right
            y: 1
            height: 1
            color: Theme.Theme.bevel
        }
        Rectangle {
            x: 1
            y: 1
            width: 1
            height: parent.height - 2
            color: Theme.Theme.bevel
        }
    }
}
