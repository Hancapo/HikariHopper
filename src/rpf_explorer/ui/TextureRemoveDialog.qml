import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: qsTr("REMOVE TEXTURE")
    bodyHeight: 160
    applyLabel: qsTr("Remove")
    applyAction: function() { return bridge.removeSelected() }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 12
        spacing: 10

        TextureDialogSummary { Layout.fillWidth: true; bridge: dialog.bridge }
        Rectangle { Layout.fillWidth: true; Layout.preferredHeight: 1; color: Theme.Theme.border }
        Text {
            Layout.fillWidth: true
            text: qsTr("Remove this texture from the working YTD? The source file is not changed until you save.")
            color: Theme.Theme.textRow
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
            wrapMode: Text.WordWrap
        }
        Item { Layout.fillHeight: true }
    }
}
