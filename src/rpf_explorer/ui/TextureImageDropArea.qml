import QtQuick
import "theme" as Theme

DropArea {
    id: dropArea

    required property var bridge
    property bool acceptableDrag: false

    keys: ["text/uri-list"]
    enabled: dropArea.bridge.canImportImages

    onEntered: function(drag) {
        acceptableDrag = drag.hasUrls && dropArea.bridge.canAcceptImageDrop(drag.urls)
        drag.accepted = acceptableDrag
    }
    onExited: acceptableDrag = false
    onDropped: function(drop) {
        acceptableDrag = false
        if (drop.hasUrls && dropArea.bridge.importDroppedImages(drop.urls))
            drop.accept(Qt.CopyAction)
        else
            drop.accepted = false
    }

    Rectangle {
        anchors.fill: parent
        visible: dropArea.containsDrag && dropArea.acceptableDrag
        color: Theme.Theme.selectionWash
        border.width: 1
        border.color: Theme.Theme.selectionRing
        Accessible.ignored: true

        Text {
            anchors.centerIn: parent
            text: qsTr("DROP IMAGES TO IMPORT")
            color: Theme.Theme.selection
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.fontSize
            font.bold: true
        }
    }
}
