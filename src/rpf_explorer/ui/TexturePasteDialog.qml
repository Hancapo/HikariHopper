import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog
    objectName: "texturePasteDialog"

    property bool replacementAvailable: false
    property string replacementName: ""
    readonly property bool replaceMode: modeCombo.currentValue === "replace"
    readonly property string nameError: replaceMode ? "" : dialog.bridge.pasteNameError(nameField.text)

    heading: qsTr("PASTE TEXTURE")
    bodyHeight: 150
    applyLabel: qsTr("Paste")
    applyEnabled: dialog.bridge.pastePending && (replaceMode ? dialog.bridge.pasteCanReplace : nameError === "")
    applyAction: function() { return dialog.bridge.pasteImage(dialog.replaceMode, nameField.text) }
    onRejected: dialog.bridge.cancelPasteImage()
    onOpened: {
        // Initialize once: clipboard notifications must not reset a user's choice.
        replacementAvailable = bridge.pasteCanReplace
        replacementName = bridge.pasteTargetName
        modeCombo.currentIndex = replacementAvailable ? 0 : 1
        if (replaceMode)
            modeCombo.forceActiveFocus()
        else {
            nameField.forceActiveFocus()
            nameField.selectAll()
        }
    }

    Connections {
        target: dialog.bridge
        function onStateChanged() {
            if (!dialog.bridge.pastePending)
                dialog.close()
        }
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 12
        spacing: 10
        GridLayout {
            Layout.fillWidth: true
            columns: 2
            columnSpacing: 12
            rowSpacing: 10
            Text { text: qsTr("Action"); color: Theme.Theme.textRow; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            FlatComboBox {
                id: modeCombo
                objectName: "pasteMode"
                Layout.fillWidth: true
                valueRole: "value"
                model: [
                    { label: qsTr("Replace selected texture"), value: "replace", supported: dialog.replacementAvailable },
                    { label: qsTr("Add new texture"), value: "add" }
                ]
            }
            Text { text: qsTr("Name"); color: Theme.Theme.textRow; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            FlatTextField {
                id: nameField
                objectName: "pasteName"
                Layout.fillWidth: true
                Layout.preferredHeight: 28
                enabled: !dialog.replaceMode
                maximumLength: 255
                text: dialog.replaceMode ? dialog.replacementName : "pasted_texture"
                invalid: dialog.nameError !== ""
                Accessible.name: qsTr("New texture name")
            }
        }
        Text {
            Layout.fillWidth: true
            text: dialog.nameError
            visible: text !== ""
            color: Theme.Theme.error
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
            wrapMode: Text.WordWrap
        }
        Item { Layout.fillHeight: true }
    }
}
