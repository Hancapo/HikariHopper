import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

/*
 * The keyboard reference, laid out as a table rather than a paragraph of
 * newline-separated text. Keys go in mono at row weight, actions in the UI face
 * — the same key-then-action pairing the status bar uses, so the two teach each
 * other. Every entry here is a shortcut the app actually binds.
 */
ToolWindow {
    id: window

    width: 470
    height: 545
    title: qsTr("Keyboard shortcuts")
    heading: qsTr("KEYBOARD SHORTCUTS")

    readonly property var groups: [
        {
            name: qsTr("FILE"),
            keys: [
                { key: "Ctrl+T", action: qsTr("New explorer tab") },
                { key: "Ctrl+W", action: qsTr("Close current tab") },
                { key: "Ctrl+O", action: qsTr("Open the configured game") },
                { key: "Ctrl+Shift+O", action: qsTr("Open a standalone RPF") },
                { key: "Alt+F4", action: qsTr("Exit") }
            ]
        },
        {
            name: qsTr("GO"),
            keys: [
                { key: "Alt+Left", action: qsTr("Back") },
                { key: "Alt+Right", action: qsTr("Forward") },
                { key: "Backspace", action: qsTr("Up one level") },
                { key: "Enter", action: qsTr("Open the selected entry") }
            ]
        },
        {
            name: qsTr("EDIT"),
            keys: [
                { key: "Ctrl+A", action: qsTr("Select all visible entries") },
                { key: "Ctrl+C", action: qsTr("Copy name") },
                { key: "Ctrl+Shift+C", action: qsTr("Copy path") },
                { key: "Delete", action: qsTr("Delete selected files") },
                { key: "F3 · Ctrl+F", action: qsTr("Search this folder") },
                { key: "Esc", action: qsTr("Clear the search") }
            ]
        }
    ]

    Flickable {
        anchors.fill: parent
        contentHeight: table.implicitHeight
        clip: true
        boundsBehavior: Flickable.StopAtBounds

        ColumnLayout {
            id: table
            width: parent.width
            spacing: 0

            Repeater {
                model: window.groups
                delegate: ColumnLayout {
                    id: group
                    required property var modelData
                    required property int index
                    Layout.fillWidth: true
                    spacing: 0

                    DottedRule {
                        Layout.fillWidth: true
                        Layout.topMargin: group.index === 0 ? 0 : 16
                        Layout.bottomMargin: 8
                        horizontal: true
                        visible: group.index > 0
                    }

                    Text {
                        Layout.bottomMargin: 6
                        text: group.modelData.name
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: 9
                        font.bold: true
                        font.letterSpacing: 1.2
                    }

                    Repeater {
                        model: group.modelData.keys
                        delegate: RowLayout {
                            id: row
                            required property var modelData
                            Layout.fillWidth: true
                            Layout.preferredHeight: 24
                            spacing: 14

                            Text {
                                Layout.preferredWidth: 116
                                text: row.modelData.key
                                color: Theme.Theme.textRow
                                font.family: Theme.Theme.monoFont
                                font.pixelSize: Theme.Theme.fontSize
                                verticalAlignment: Text.AlignVCenter
                            }
                            Text {
                                Layout.fillWidth: true
                                text: row.modelData.action
                                color: Theme.Theme.textDim
                                font.family: Theme.Theme.uiFont
                                font.pixelSize: Theme.Theme.fontSize
                                verticalAlignment: Text.AlignVCenter
                                elide: Text.ElideRight
                            }
                        }
                    }
                }
            }
        }
    }
}
