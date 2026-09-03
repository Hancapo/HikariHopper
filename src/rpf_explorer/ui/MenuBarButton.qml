import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

ToolButton {
    id: control

    property bool menuOpen: false

    implicitWidth: Math.ceil(label.implicitWidth) + leftPadding + rightPadding
    implicitHeight: Theme.Theme.menuHeight
    leftPadding: 12
    rightPadding: 12
    topPadding: 0
    bottomPadding: 0
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus

    background: Rectangle {
        color: control.menuOpen
            ? Theme.Theme.chromeRaised
            : control.down ? Theme.Theme.borderSoft
            : control.hovered ? Theme.Theme.hoverChrome
            : "transparent"

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            height: 1
            visible: control.menuOpen
            color: Theme.Theme.bevel
        }

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            visible: control.menuOpen
            color: Theme.Theme.chromeRaised
        }

        Rectangle {
            anchors.horizontalCenter: parent.horizontalCenter
            anchors.bottom: parent.bottom
            anchors.bottomMargin: 2
            width: 12
            height: 1
            visible: control.activeFocus && !control.menuOpen
            color: Theme.Theme.textRow
        }
    }

    contentItem: Text {
        id: label
        text: control.text
        color: control.menuOpen || control.hovered || control.down || control.activeFocus
            ? Theme.Theme.text
            : Theme.Theme.textRow
        font.family: Theme.Theme.uiFont
        font.pixelSize: Theme.Theme.fontSize
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
    }
}
