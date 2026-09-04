import QtQuick
import "theme" as Theme

DropArea {
    id: dropArea

    required property var bridge
    property bool acceptableDrag: false

    keys: ["text/uri-list"]
    enabled: dropArea.bridge.hasWorkspace
        && !dropArea.bridge.entryOperationBusy

    function accepts(drag) {
        return drag.hasUrls
            && drag.formats.indexOf(dropArea.bridge.entryDragMimeType) < 0
            && dropArea.enabled
    }

    onEntered: function(drag) {
        acceptableDrag = accepts(drag)
        drag.accepted = acceptableDrag
    }
    onExited: acceptableDrag = false
    onDropped: function(drop) {
        const accepted = accepts(drop)
        acceptableDrag = false
        if (!accepted) {
            drop.accepted = false
            return
        }
        if (dropArea.bridge.importDroppedFiles(drop.urls))
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

        Text {
            anchors.centerIn: parent
            text: qsTr("DROP FILES TO IMPORT")
            color: Theme.Theme.selection
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.fontSize
            font.bold: true
            font.letterSpacing: 1
        }
    }
}
