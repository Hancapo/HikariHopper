import QtQuick
import "theme" as Theme

TextureToolDialog {
    id: dialog
    objectName: "textureImportConfirmation"

    heading: qsTr("IMPORT TEXTURES")
    bodyHeight: 110
    applyLabel: qsTr("Import")
    applyEnabled: dialog.bridge.importConfirmationPending
    applyAction: function() { return dialog.bridge.confirmImageImport(true) }
    onRejected: dialog.bridge.confirmImageImport(false)

    Connections {
        target: dialog.bridge
        function onStateChanged() {
            if (!dialog.bridge.importConfirmationPending)
                dialog.close()
        }
    }

    Text {
        anchors.fill: parent
        anchors.margins: 16
        text: qsTr("Import %1 files? %2 matching names will be replaced for the entire batch.\n\nIf files share a name, the last file wins.")
            .arg(dialog.bridge.importFileCount).arg(dialog.bridge.importConflictCount)
        color: Theme.Theme.textRow
        font.family: Theme.Theme.uiFont
        font.pixelSize: Theme.Theme.fontSize
        wrapMode: Text.WordWrap
    }
}
