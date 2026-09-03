import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: qsTr("REPAIR ALPHA EDGES")
    bodyHeight: 240
    applyAction: function() {
        return bridge.repairSelectedAlphaEdges(radiusCombo.currentValue)
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 12
        spacing: 10

        TextureDialogSummary { Layout.fillWidth: true; bridge: dialog.bridge }
        Rectangle { Layout.fillWidth: true; Layout.preferredHeight: 1; color: Theme.Theme.border }

        Text {
            Layout.fillWidth: true
            text: qsTr("Repairs dark RGB bleeding around transparent pixels while preserving the stored alpha chain.")
            color: Theme.Theme.textRow
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
            wrapMode: Text.WordWrap
        }

        RowLayout {
            Layout.fillWidth: true
            spacing: 12
            Text { Layout.preferredWidth: 112; text: qsTr("Repair radius"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            FlatComboBox {
                id: radiusCombo
                Layout.fillWidth: true
                valueRole: "value"
                currentIndex: 1
                model: [
                    { label: qsTr("2 pixels"), value: 2 },
                    { label: qsTr("4 pixels"), value: 4 },
                    { label: qsTr("8 pixels"), value: 8 }
                ]
            }
        }

        Item { Layout.fillHeight: true }
    }
}
