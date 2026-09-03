import QtQuick
import QtQuick.Layouts
import "theme" as Theme

RowLayout {
    id: summary

    required property var bridge

    spacing: 10

    LucideIcon {
        Layout.preferredWidth: 20
        Layout.preferredHeight: 20
        name: "image"
        stroke: Theme.Theme.inkAsset
        Accessible.ignored: true
    }

    ColumnLayout {
        Layout.fillWidth: true
        spacing: 1

        Text {
            Layout.fillWidth: true
            text: summary.bridge.selectedName
            color: Theme.Theme.text
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.fontSize
            font.bold: true
            elide: Text.ElideRight
        }

        Text {
            Layout.fillWidth: true
            text: qsTr("Original  %1  ·  %2  ·  %3 mips")
                .arg(summary.bridge.selectedDimensions)
                .arg(summary.bridge.selectedFormat)
                .arg(summary.bridge.mipCount)
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            elide: Text.ElideRight
        }
    }
}
