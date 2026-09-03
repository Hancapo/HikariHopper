import QtQuick
import "theme" as Theme

Rectangle {
    id: row

    required property string label
    required property string note
    property bool selected: false
    property bool available: true

    implicitHeight: 44
    color: selected ? Theme.Theme.selection : "transparent"

    Rectangle {
        anchors.left: parent.left
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        width: 2
        visible: row.selected
        color: Theme.Theme.selectionText
    }

    Text {
        anchors.left: parent.left
        anchors.leftMargin: 12
        y: 7
        width: parent.width - 24
        text: row.label
        color: row.selected
            ? Theme.Theme.selectionText
            : Theme.Theme.textFaint
        opacity: row.available ? 1 : 0.7
        font.family: Theme.Theme.uiFont
        font.pixelSize: Theme.Theme.fontSize
        font.bold: row.selected
        elide: Text.ElideRight
    }

    Text {
        anchors.left: parent.left
        anchors.leftMargin: 12
        y: 25
        text: row.note
        color: row.selected
            ? Theme.Theme.selectionInk
            : Theme.Theme.textFaint
        opacity: row.available ? 1 : 0.7
        font.family: Theme.Theme.monoFont
        font.pixelSize: 9
    }
}
