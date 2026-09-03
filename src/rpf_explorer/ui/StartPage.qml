import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

/*
 * The empty state for a tab with no workspace.
 *
 * The app wears its own chrome from the first frame — the navigation bar and the
 * column header are present but inert — and the picker sits centred in the area
 * where the entries will appear. It is the tool waiting, not a splash screen:
 * no headline, no marketing sentence, no text links.
 */
Rectangle {
    id: page
    required property var tabs
    required property var bridge
    color: Theme.Theme.appBg

    // Settings owns installation discovery. This picker only chooses which of
    // the two configured editions to open.
    readonly property var titles: [
        { label: qsTr("Grand Theft Auto V — Enhanced"), edition: "enhanced" },
        { label: qsTr("Grand Theft Auto V — Legacy"), edition: "legacy" }
    ]

    readonly property string selectedEdition: titleBox.currentIndex < 0
        ? ""
        : page.titles[titleBox.currentIndex].edition
    readonly property bool selectedPathValid: selectedEdition === "enhanced"
        ? page.tabs.gamePathSettings.enhancedPathValid
        : selectedEdition === "legacy" && page.tabs.gamePathSettings.legacyPathValid

    function openSelected() {
        page.bridge.openConfiguredGame(page.selectedEdition)
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        // An inert navigation bar, so the toolbar does not appear out of nowhere
        // the moment a workspace loads.
        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: Theme.Theme.navigationHeight
            color: Theme.Theme.navigationBg

            Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; height: 1; color: Theme.Theme.bevel }
            Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 7
                anchors.rightMargin: 8
                spacing: 6

                RowLayout {
                    spacing: 1
                    Repeater {
                        model: ["back", "forward", "up"]
                        delegate: ChromeToolButton {
                            id: navButton
                            required property string modelData
                            Layout.preferredWidth: 28
                            Layout.preferredHeight: 28
                            bordered: false
                            enabled: false
                            iconKind: navButton.modelData
                        }
                    }
                }

                Rectangle {
                    Layout.fillWidth: true
                    Layout.preferredHeight: 28
                    color: Theme.Theme.insetBg
                    border.width: 1
                    border.color: Theme.Theme.borderHard
                    Text {
                        anchors.left: parent.left
                        anchors.leftMargin: 12
                        anchors.verticalCenter: parent.verticalCenter
                        text: qsTr("No installation open")
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.fontSize
                    }
                }

                Rectangle {
                    Layout.preferredWidth: 216
                    Layout.preferredHeight: 28
                    color: Theme.Theme.insetBg
                    border.width: 1
                    border.color: Theme.Theme.borderHard
                    ChromeIcon {
                        anchors.left: parent.left
                        anchors.leftMargin: 8
                        anchors.verticalCenter: parent.verticalCenter
                        kind: "search"
                        stroke: Theme.Theme.textFaint
                        implicitWidth: 13
                        implicitHeight: 13
                    }
                }
            }
        }

        // The column header, spanning the full width.
        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: Theme.Theme.headerHeight
            color: Theme.Theme.headerBg

            Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; height: 1; color: Theme.Theme.bevel }
            Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }

            RowLayout {
                anchors.fill: parent
                spacing: 0

                Item {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    Text {
                        anchors.left: parent.left
                        anchors.leftMargin: 38
                        anchors.verticalCenter: parent.verticalCenter
                        text: qsTr("NAME")
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.smallFontSize
                        font.bold: true
                        font.letterSpacing: 0.9
                    }
                    DottedRule { anchors.right: parent.right; anchors.top: parent.top; anchors.bottom: parent.bottom }
                }
                Item {
                    Layout.preferredWidth: 190
                    Layout.fillHeight: true
                    Text {
                        anchors.fill: parent
                        leftPadding: 10
                        text: qsTr("TYPE")
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.smallFontSize
                        font.bold: true
                        font.letterSpacing: 0.9
                        verticalAlignment: Text.AlignVCenter
                    }
                    DottedRule { anchors.right: parent.right; anchors.top: parent.top; anchors.bottom: parent.bottom }
                }
                Item {
                    Layout.preferredWidth: 124
                    Layout.fillHeight: true
                    Text {
                        anchors.fill: parent
                        rightPadding: 12
                        text: qsTr("SIZE")
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.smallFontSize
                        font.bold: true
                        font.letterSpacing: 0.9
                        horizontalAlignment: Text.AlignRight
                        verticalAlignment: Text.AlignVCenter
                    }
                }
            }
        }

        // The picker, centred where the entries will be.
        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ColumnLayout {
                anchors.centerIn: parent
                anchors.verticalCenterOffset: -70
                width: Math.min(560, parent.width - 64)
                spacing: 10

                RowLayout {
                    Layout.fillWidth: true
                    spacing: 10

                    Text {
                        text: qsTr("Game")
                        color: Theme.Theme.textDim
                        font.family: Theme.Theme.uiFont
                        font.pixelSize: Theme.Theme.fontSize
                        verticalAlignment: Text.AlignVCenter
                    }
                    FlatComboBox {
                        id: titleBox
                        objectName: "titleBox"
                        Layout.fillWidth: true
                        model: page.titles
                    }
                    ChromeToolButton {
                        Layout.preferredWidth: 92
                        Layout.preferredHeight: 28
                        enabled: page.selectedPathValid
                        objectName: "openButton"
                        primary: true
                        text: qsTr("Open", "action: open configured game")
                        Accessible.name: qsTr("Open the configured game")
                        onClicked: page.openSelected()
                    }
                }

                Text {
                    Layout.fillWidth: true
                    Layout.leftMargin: 48
                    visible: !page.selectedPathValid
                    text: qsTr("Set this edition's installation path in Edit > Settings.")
                    color: Theme.Theme.textFaint
                    font.family: Theme.Theme.uiFont
                    font.pixelSize: Theme.Theme.smallFontSize
                    wrapMode: Text.WordWrap
                }

                // Recents use the entry table's row language, not a card.
                ColumnLayout {
                    Layout.fillWidth: true
                    Layout.topMargin: 4
                    spacing: 0
                    visible: page.tabs.recentGames.length > 0

                    DottedRule { Layout.fillWidth: true; Layout.bottomMargin: 9; horizontal: true }

                    Text {
                        Layout.bottomMargin: 4
                        text: qsTr("RECENT")
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: 9
                        font.bold: true
                        font.letterSpacing: 1.2
                    }

                    Repeater {
                        model: page.tabs.recentGames
                        delegate: Rectangle {
                            id: recentRow
                            required property var modelData
                            Layout.fillWidth: true
                            Layout.preferredHeight: Theme.Theme.rowHeight
                            color: recentHover.containsMouse ? Theme.Theme.hoverBg : "transparent"

                            RowLayout {
                                anchors.fill: parent
                                spacing: 8
                                FileGlyph { Layout.leftMargin: 2; kind: "folder" }
                                Text {
                                    Layout.preferredWidth: 104
                                    elide: Text.ElideRight
                                    text: recentRow.modelData.edition
                                    color: Theme.Theme.textRow
                                    font.family: Theme.Theme.monoFont
                                    font.pixelSize: Theme.Theme.smallFontSize
                                    verticalAlignment: Text.AlignVCenter
                                }
                                Text {
                                    Layout.fillWidth: true
                                    text: recentRow.modelData.path
                                    color: Theme.Theme.textFaint
                                    font.family: Theme.Theme.monoFont
                                    font.pixelSize: Theme.Theme.smallFontSize
                                    verticalAlignment: Text.AlignVCenter
                                    elide: Text.ElideLeft
                                }
                            }

                            MouseArea {
                                id: recentHover
                                anchors.fill: parent
                                hoverEnabled: true
                                cursorShape: Qt.PointingHandCursor
                                onClicked: page.tabs.openRecentGame(recentRow.modelData.path)
                            }
                            Accessible.role: Accessible.Button
                            Accessible.name: qsTr("Open %1").arg(recentRow.modelData.path)
                        }
                    }
                }

                DottedRule { Layout.fillWidth: true; Layout.topMargin: 5; horizontal: true }

                ChromeToolButton {
                    Layout.preferredWidth: 168
                    Layout.preferredHeight: 28
                    Layout.topMargin: 4
                    objectName: "standaloneButton"
                    raised: true
                    text: qsTr("Open standalone RPF…")
                    onClicked: page.bridge.openArchiveDialog()
                }
            }
        }
    }

    Text {
        anchors.left: parent.left
        anchors.bottom: parent.bottom
        anchors.margins: 10
        text: page.bridge.status
        color: Theme.Theme.textFaint
        font.family: Theme.Theme.monoFont
        font.pixelSize: Theme.Theme.smallFontSize
    }
}
