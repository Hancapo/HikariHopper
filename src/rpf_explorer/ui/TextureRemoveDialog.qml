import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: bridge.selectedCount > 1 ? qsTr("REMOVE TEXTURES") : qsTr("REMOVE TEXTURE")
    bodyHeight: 160
    applyLabel: qsTr("Remove")
    applyEnabled: bridge.canRemoveSelection
    applyAction: function() { return bridge.removeSelected() }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 12
        spacing: 10

        TextureDialogSummary { Layout.fillWidth: true; bridge: dialog.bridge }
        Rectangle { Layout.fillWidth: true; Layout.preferredHeight: 1; color: Theme.Theme.border }
        Text {
            Layout.fillWidth: true
            text: dialog.bridge.selectedCount > 1
                ? qsTr("Remove %1 selected textures?").arg(dialog.bridge.selectedCount)
                : qsTr("Remove this texture?")
            color: Theme.Theme.textRow
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
            wrapMode: Text.WordWrap
        }
        Item { Layout.fillHeight: true }
    }
}
