import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: qsTr("RENAME TEXTURE")
    bodyHeight: 158
    applyLabel: qsTr("Rename")
    applyEnabled: nameField.text.trim().length > 0
    applyAction: function() { return bridge.renameSelected(nameField.text) }

    onOpened: {
        nameField.text = bridge.selectedName
        nameField.forceActiveFocus()
        nameField.selectAll()
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 12
        spacing: 10

        TextureDialogSummary { Layout.fillWidth: true; bridge: dialog.bridge }
        Rectangle { Layout.fillWidth: true; Layout.preferredHeight: 1; color: Theme.Theme.border }
        RowLayout {
            Layout.fillWidth: true
            spacing: 12
            Text { Layout.preferredWidth: 112; text: qsTr("Texture name"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            FlatTextField { id: nameField; Layout.fillWidth: true; maximumLength: 255 }
        }
        Item { Layout.fillHeight: true }
    }
}
