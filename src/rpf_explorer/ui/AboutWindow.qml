import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

/*
 * About. Facts in the same key/value grammar the preview panel uses, so it reads
 * as part of the tool rather than as a splash.
 */
ToolWindow {
    id: window

    width: 460
    height: 340
    title: qsTr("About HikariHopper")
    heading: qsTr("ABOUT")

    readonly property var facts: [
        { k: qsTr("Version"), v: "0.1.0" },
        { k: qsTr("Reads"), v: qsTr("GTA V Legacy and Enhanced") },
        { k: qsTr("Archives"), v: qsTr("RPF, including nested") }
    ]

    readonly property var credits: [
        { k: "FiveFury", v: qsTr("Archive parsing and game keys") },
        { k: "PySide6", v: qsTr("Qt bindings") },
        { k: "Archivo · Space Mono", v: qsTr("Typefaces, SIL Open Font License") }
    ]

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        RowLayout {
            Layout.fillWidth: true
            Layout.bottomMargin: 18
            spacing: 12

            Rectangle {
                Layout.preferredWidth: 26
                Layout.preferredHeight: 26
                color: Theme.Theme.brass
                ChromeIcon {
                    anchors.centerIn: parent
                    kind: "package"
                    stroke: Theme.Theme.windowChrome
                    thickness: 1.8
                    implicitWidth: 17
                    implicitHeight: 17
                }
            }

            ColumnLayout {
                spacing: 2
                Text {
                    text: "HikariHopper"
                    color: Theme.Theme.text
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: 16
                    font.bold: true
                }
                Text {
                    text: qsTr("RPF Explorer")
                    color: Theme.Theme.textFaint
                    font.family: Theme.Theme.uiFont
                    font.pixelSize: Theme.Theme.smallFontSize
                }
            }
        }

        Repeater {
            model: window.facts
            delegate: RowLayout {
                id: fact
                required property var modelData
                Layout.fillWidth: true
                Layout.preferredHeight: 24
                spacing: 14
                Text {
                    Layout.preferredWidth: 84
                    text: fact.modelData.k
                    color: Theme.Theme.textDim
                    font.family: Theme.Theme.uiFont
                    font.pixelSize: Theme.Theme.fontSize
                    verticalAlignment: Text.AlignVCenter
                }
                Text {
                    Layout.fillWidth: true
                    text: fact.modelData.v
                    color: Theme.Theme.text
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: 11
                    verticalAlignment: Text.AlignVCenter
                    elide: Text.ElideRight
                }
            }
        }

        DottedRule { Layout.fillWidth: true; Layout.topMargin: 16; Layout.bottomMargin: 8; horizontal: true }

        Text {
            Layout.bottomMargin: 6
            text: qsTr("BUILT ON")
            color: Theme.Theme.textFaint
            font.family: Theme.Theme.monoFont
            font.pixelSize: 9
            font.bold: true
            font.letterSpacing: 1.2
        }

        Repeater {
            model: window.credits
            delegate: RowLayout {
                id: credit
                required property var modelData
                Layout.fillWidth: true
                Layout.preferredHeight: 22
                spacing: 14
                Text {
                    Layout.preferredWidth: 150
                    text: credit.modelData.k
                    color: Theme.Theme.textRow
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: Theme.Theme.smallFontSize
                    verticalAlignment: Text.AlignVCenter
                }
                Text {
                    Layout.fillWidth: true
                    text: credit.modelData.v
                    color: Theme.Theme.textFaint
                    font.family: Theme.Theme.uiFont
                    font.pixelSize: Theme.Theme.smallFontSize
                    verticalAlignment: Text.AlignVCenter
                    elide: Text.ElideRight
                }
            }
        }

        Item { Layout.fillHeight: true }
    }
}
