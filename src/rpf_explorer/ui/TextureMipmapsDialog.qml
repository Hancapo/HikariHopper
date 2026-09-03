import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: qsTr("RECALCULATE MIPMAPS")
    bodyHeight: 250
    applyAction: function() {
        return bridge.recalculateSelectedMipmaps(
            filterCombo.currentValue,
            mipSizeCombo.currentValue
        )
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

            Text { text: qsTr("Downsampling"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            TextureFilterCombo { id: filterCombo; Layout.fillWidth: true }
            Text { text: qsTr("Smallest level"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            TextureMipSizeCombo { id: mipSizeCombo; Layout.fillWidth: true }
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 56
            color: Theme.Theme.insetBg
            border.width: 1
            border.color: Theme.Theme.border
            Text { x: 9; y: 4; text: qsTr("RESULT"); color: Theme.Theme.textFaint; font.family: Theme.Theme.monoFont; font.pixelSize: Theme.Theme.smallFontSize; font.bold: true; font.letterSpacing: 1 }
            Text {
                x: 9; y: 25; width: parent.width - 18
                text: qsTr("%1 mips  →  %2 mips  ·  last %3")
                    .arg(dialog.bridge.mipCount)
                    .arg(dialog.bridge.estimatedMipCount(dialog.bridge.selectedWidth, dialog.bridge.selectedHeight, mipSizeCombo.currentValue))
                    .arg(dialog.bridge.estimatedSmallestMipDimensions(dialog.bridge.selectedWidth, dialog.bridge.selectedHeight, mipSizeCombo.currentValue))
                color: Theme.Theme.textRow
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.fontSize
            }
        }

        Item { Layout.fillHeight: true }
    }
}
