import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: factsBar

    required property var bridge

    readonly property var facts: [
        { label: qsTr("DIMENSIONS"), value: bridge.selectedDimensions },
        { label: qsTr("FORMAT"), value: bridge.selectedFormat },
        { label: qsTr("USAGE"), value: bridge.selectedUsage },
        { label: qsTr("DATA"), value: bridge.selectedDataSize }
    ]

    implicitHeight: Theme.Theme.textureFactsHeight
    color: Theme.Theme.chromeBg

    RowLayout {
        anchors.fill: parent
        spacing: 0

        Repeater {
            model: factsBar.facts
            delegate: Rectangle {
                id: fact
                required property var modelData
                Layout.fillWidth: true
                Layout.fillHeight: true
                color: Theme.Theme.chromeBg
                border.width: 1
                border.color: Theme.Theme.borderHard

                Text {
                    x: 10
                    y: 6
                    text: fact.modelData.label
                    color: Theme.Theme.textFaint
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: Theme.Theme.smallFontSize
                    font.bold: true
                }

                Text {
                    x: 10
                    y: 25
                    text: fact.modelData.value
                    color: Theme.Theme.textRow
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: Theme.Theme.fontSize
                }
            }
        }

        Rectangle {
            Layout.preferredWidth: 150
            Layout.fillHeight: true
            color: Theme.Theme.chromeBg

            ChromeToolButton {
                anchors.centerIn: parent
                width: 132
                height: 30
                primary: true
                enabled: factsBar.bridge.selectedIndex >= 0
                text: qsTr("Extract DDS…")
                onClicked: factsBar.bridge.extractSelected()
            }
        }
    }
}
