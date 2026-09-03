import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: qsTr("CHANGE TEXTURE FORMAT")
    bodyHeight: 292
    applyAction: function() {
        return bridge.changeSelectedFormat(
            formatCombo.currentValue,
            qualityCombo.currentValue,
            filterCombo.currentValue,
            mipSizeCombo.currentValue
        )
    }

    onOpened: {
        for (let index = 0; index < formatCombo.count; ++index) {
            if (formatCombo.valueAt(index) === bridge.selectedFormat.replace(/^.*\((.*)\)$/, "$1")) {
                formatCombo.currentIndex = index
                break
            }
        }
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 12
        spacing: 10

        TextureDialogSummary { Layout.fillWidth: true; bridge: dialog.bridge }
        Rectangle { Layout.fillWidth: true; Layout.preferredHeight: 1; color: Theme.Theme.border }

        GridLayout {
            Layout.fillWidth: true
            columns: 2
            columnSpacing: 12
            rowSpacing: 8

            Text { text: qsTr("Target format"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            FlatComboBox { id: formatCombo; Layout.fillWidth: true; valueRole: "value"; model: dialog.bridge.formatOptions }
            Text { text: qsTr("Compression"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            TextureQualityCombo { id: qualityCombo; Layout.fillWidth: true }
            Text { text: qsTr("Downsampling"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            TextureFilterCombo { id: filterCombo; Layout.fillWidth: true }
            Text { text: qsTr("Smallest mip"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            TextureMipSizeCombo { id: mipSizeCombo; Layout.fillWidth: true }
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 48
            color: Theme.Theme.insetBg
            border.width: 1
            border.color: Theme.Theme.border
            Text { x: 9; y: 4; text: qsTr("RESULT"); color: Theme.Theme.textFaint; font.family: Theme.Theme.monoFont; font.pixelSize: Theme.Theme.smallFontSize; font.bold: true; font.letterSpacing: 1 }
            Text { x: 9; y: 23; width: parent.width - 18; text: qsTr("%1  →  %2").arg(dialog.bridge.selectedFormat).arg(formatCombo.currentText); color: Theme.Theme.textRow; font.family: Theme.Theme.monoFont; font.pixelSize: Theme.Theme.fontSize; elide: Text.ElideRight }
        }

        Item { Layout.fillHeight: true }
    }
}
