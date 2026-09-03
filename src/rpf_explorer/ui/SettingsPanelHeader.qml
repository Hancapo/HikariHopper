import QtQuick
import "theme" as Theme

Rectangle {
    id: header

    required property string label

    implicitHeight: Theme.Theme.headerHeight
    color: Theme.Theme.chromeRaised

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
        height: 1
        color: Theme.Theme.bevel
    }

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        height: 1
        color: Theme.Theme.borderHard
    }

    Text {
        anchors.left: parent.left
        anchors.leftMargin: 12
        anchors.verticalCenter: parent.verticalCenter
        text: header.label
        color: Theme.Theme.textDim
        font.family: Theme.Theme.monoFont
        font.pixelSize: Theme.Theme.smallFontSize
        font.bold: true
        font.letterSpacing: 1
    }
}
