import QtQuick
import QtQuick.Layouts
import "theme" as Theme

TextureToolDialog {
    id: dialog

    property bool powerOfTwo: false
    readonly property var roundedSize: {
        // Read selection properties here so the preview follows the selected texture.
        const width = bridge.selectedWidth
        const height = bridge.selectedHeight
        return powerOfTwo && width > 0 && height > 0
            ? bridge.powerOfTwoSize(roundingCombo.currentValue || "nearest") : ({})
    }
    readonly property int targetWidth: powerOfTwo ? (roundedSize.width || 0) : Number(widthField.text)
    readonly property int targetHeight: powerOfTwo ? (roundedSize.height || 0) : Number(heightField.text)
    readonly property bool validSize: targetWidth > 0 && targetHeight > 0
        && targetWidth <= bridge.maximumDimension && targetHeight <= bridge.maximumDimension
        && (powerOfTwo
            ? (bridge.selectedCount > 0 && bridge.validPowerOfTwoSelection(roundingCombo.currentValue || "nearest"))
            : (widthField.acceptableInput && heightField.acceptableInput))

    heading: powerOfTwo ? qsTr("RESIZE TO POWER OF 2") : qsTr("RESIZE TEXTURE")
    bodyHeight: 330
    applyEnabled: validSize && (!powerOfTwo || bridge.selectedNeedsPowerOfTwo)
    applyAction: function() {
        if (powerOfTwo)
            return bridge.resizeSelectionToPowerOfTwo(roundingCombo.currentValue,
                filterCombo.currentValue, mipSizeCombo.currentValue, mipCheck.checked)
        return bridge.resizeSelected(
            targetWidth,
            targetHeight,
            filterCombo.currentValue,
            mipSizeCombo.currentValue,
            mipCheck.checked
        )
    }

    onOpened: {
        widthField.text = bridge.selectedWidth.toString()
        heightField.text = bridge.selectedHeight.toString()
        if (powerOfTwo) {
            roundingCombo.forceActiveFocus()
        } else {
            widthField.forceActiveFocus()
            widthField.selectAll()
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

            Text { visible: !dialog.powerOfTwo; text: qsTr("Resolution"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            RowLayout {
                visible: !dialog.powerOfTwo
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

            Item { visible: !dialog.powerOfTwo; Layout.preferredWidth: 112; Layout.preferredHeight: 1 }
            SquareCheckBox { id: lockAspect; visible: !dialog.powerOfTwo; Layout.fillWidth: true; text: qsTr("Lock aspect ratio"); checked: true }

            Text { visible: dialog.powerOfTwo; text: qsTr("Rounding"); color: Theme.Theme.textFaint; font.family: Theme.Theme.uiFont; font.pixelSize: Theme.Theme.fontSize }
            FlatComboBox {
                id: roundingCombo
                objectName: "powerOfTwoRounding"
                visible: dialog.powerOfTwo
                Layout.fillWidth: true
                valueRole: "value"
                model: [
                    { label: qsTr("Nearest"), value: "nearest" },
                    { label: qsTr("Round down"), value: "down" },
                    { label: qsTr("Round up"), value: "up" }
                ]
            }

            Text {
                visible: dialog.powerOfTwo
                Layout.columnSpan: 2
                Layout.fillWidth: true
                text: qsTr("Each dimension is rounded separately; proportions may change.")
                color: Theme.Theme.textDim
                font.family: Theme.Theme.uiFont
                font.pixelSize: Theme.Theme.smallFontSize
                wrapMode: Text.WordWrap
            }

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
                text: !dialog.validSize && dialog.powerOfTwo
                    ? qsTr("Result exceeds the maximum dimension (%1). Choose Round down.").arg(dialog.bridge.maximumDimension)
                    : dialog.bridge.selectedCount > 1
                    ? (dialog.powerOfTwo
                        ? qsTr("Round each texture independently · %1").arg(roundingCombo.currentText)
                        : qsTr("All selected textures → %1 × %2").arg(dialog.targetWidth).arg(dialog.targetHeight))
                    : qsTr("%1 × %2  →  %3 × %4  ·  %5 mips  ·  last %6")
                    .arg(dialog.bridge.selectedWidth)
                    .arg(dialog.bridge.selectedHeight)
                    .arg(dialog.targetWidth || "—")
                    .arg(dialog.targetHeight || "—")
                    .arg(mipCheck.checked && dialog.validSize
                        ? dialog.bridge.estimatedMipCount(dialog.targetWidth, dialog.targetHeight, mipSizeCombo.currentValue)
                        : 1)
                    .arg(mipCheck.checked && dialog.validSize
                        ? dialog.bridge.estimatedSmallestMipDimensions(dialog.targetWidth, dialog.targetHeight, mipSizeCombo.currentValue)
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
