import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    heading: qsTr("RESIZE TEXTURE")
    bodyHeight: 330
    applyEnabled: widthField.acceptableInput && heightField.acceptableInput
    applyAction: function() {
        return bridge.resizeSelected(
            Number(widthField.text),
            Number(heightField.text),
            filterCombo.currentValue,
            mipSizeCombo.currentValue,
            mipCheck.checked
        )
    }

    onOpened: {
        widthField.text = bridge.selectedWidth.toString()
        heightField.text = bridge.selectedHeight.toString()
        widthField.forceActiveFocus()
        widthField.selectAll()
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

            Text { text: qsTr("Resolution"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            RowLayout {
                Layout.fillWidth: true
                spacing: 8
                FlatTextField {
                    id: widthField
                    Layout.fillWidth: true
                    validator: IntValidator { bottom: 1; top: dialog.bridge.maximumDimension }
                    onEditingFinished: {
                        if (lockAspect.checked && dialog.bridge.selectedWidth > 0)
                            heightField.text = Math.max(1, Math.round(Number(text) * dialog.bridge.selectedHeight / dialog.bridge.selectedWidth)).toString()
                    }
                }
                Text { text: "×"; color: Theme.Theme.textDim; font.family: Theme.Theme.monoFont; font.pixelSize: Theme.Theme.fontSize }
                FlatTextField {
                    id: heightField
                    Layout.fillWidth: true
                    validator: IntValidator { bottom: 1; top: dialog.bridge.maximumDimension }
                    onEditingFinished: {
                        if (lockAspect.checked && dialog.bridge.selectedHeight > 0)
                            widthField.text = Math.max(1, Math.round(Number(text) * dialog.bridge.selectedWidth / dialog.bridge.selectedHeight)).toString()
                    }
                }
            }

            Item { Layout.preferredWidth: 112; Layout.preferredHeight: 1 }
            SquareCheckBox { id: lockAspect; Layout.fillWidth: true; text: qsTr("Lock aspect ratio"); checked: true }

            Text { text: qsTr("Resampling"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            TextureFilterCombo { id: filterCombo; Layout.fillWidth: true }

            Text { text: qsTr("Mipmaps"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            SquareCheckBox { id: mipCheck; Layout.fillWidth: true; text: qsTr("Recalculate full chain"); checked: true }

            Text { text: qsTr("Smallest level"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize; visible: mipCheck.checked }
            TextureMipSizeCombo { id: mipSizeCombo; Layout.fillWidth: true; visible: mipCheck.checked }
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
                text: qsTr("%1 × %2  →  %3 × %4  ·  %5 mips  ·  last %6")
                    .arg(dialog.bridge.selectedWidth)
                    .arg(dialog.bridge.selectedHeight)
                    .arg(widthField.text || "—")
                    .arg(heightField.text || "—")
                    .arg(mipCheck.checked && widthField.acceptableInput && heightField.acceptableInput
                        ? dialog.bridge.estimatedMipCount(Number(widthField.text), Number(heightField.text), mipSizeCombo.currentValue)
                        : 1)
                    .arg(mipCheck.checked && widthField.acceptableInput && heightField.acceptableInput
                        ? dialog.bridge.estimatedSmallestMipDimensions(Number(widthField.text), Number(heightField.text), mipSizeCombo.currentValue)
                        : qsTr("none"))
                color: Theme.Theme.textRow
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.fontSize
                elide: Text.ElideRight
            }
        }

        Item { Layout.fillHeight: true }
    }
}
