import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: header

    required property var bridge

    implicitHeight: Theme.Theme.headerHeight
    color: Theme.Theme.headerBg

    Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; height: 1; color: Theme.Theme.bevel }
    Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }

    RowLayout {
        anchors.fill: parent
        anchors.rightMargin: Theme.Theme.scrollbarWidth
        spacing: 0

        Text {
            Layout.fillWidth: true
            Layout.fillHeight: true
            leftPadding: 12
            text: qsTr("ICON FIELD")
            color: Theme.Theme.text
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
            verticalAlignment: Text.AlignVCenter
        }

        EntryTableHeaderCell {
            Layout.preferredWidth: 82
            Layout.fillHeight: true
            text: qsTr("NAME")
            leftPadding: 10
            sortActive: header.bridge.sortColumn === "name"
            sortAscending: header.bridge.sortAscending
            onClicked: header.bridge.sortEntries("name")
        }
        EntryTableHeaderCell {
            Layout.preferredWidth: 68
            Layout.fillHeight: true
            text: qsTr("TYPE")
            leftPadding: 8
            sortActive: header.bridge.sortColumn === "type"
            sortAscending: header.bridge.sortAscending
            onClicked: header.bridge.sortEntries("type")
        }
        EntryTableHeaderCell {
            Layout.preferredWidth: 68
            Layout.fillHeight: true
            text: qsTr("SIZE")
            leftPadding: 8
            sortActive: header.bridge.sortColumn === "size"
            sortAscending: header.bridge.sortAscending
            onClicked: header.bridge.sortEntries("size")
        }
    }
}
