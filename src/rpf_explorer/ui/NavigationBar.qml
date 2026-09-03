import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: navigation
    required property var bridge

    function focusSearch() {
        searchField.forceActiveFocus()
        searchField.selectAll()
    }

    Connections {
        target: navigation.bridge
        function onSearchFocusRequested() { navigation.focusSearch() }
    }

    color: Theme.Theme.navigationBg
    implicitHeight: Theme.Theme.navigationHeight
    Layout.fillWidth: true

    Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; height: 1; color: Theme.Theme.bevel }
    Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 7
        anchors.rightMargin: 8
        spacing: 6

        RowLayout {
            spacing: 1
            ChromeToolButton { Layout.preferredWidth: 28; Layout.preferredHeight: 28; bordered: false; iconKind: "back"; foreground: enabled ? Theme.Theme.textRow : Theme.Theme.textFaint; enabled: navigation.bridge.canGoBack; Accessible.name: qsTr("Back"); onClicked: navigation.bridge.goBack() }
            ChromeToolButton { Layout.preferredWidth: 28; Layout.preferredHeight: 28; bordered: false; iconKind: "forward"; foreground: enabled ? Theme.Theme.textRow : Theme.Theme.textFaint; enabled: navigation.bridge.canGoForward; Accessible.name: qsTr("Forward"); onClicked: navigation.bridge.goForward() }
            ChromeToolButton { Layout.preferredWidth: 28; Layout.preferredHeight: 28; bordered: false; iconKind: "up"; foreground: enabled ? Theme.Theme.textRow : Theme.Theme.textFaint; enabled: navigation.bridge.canGoUp; Accessible.name: qsTr("Up one folder"); onClicked: navigation.bridge.goUp() }
        }

        // Address bar. The path reads as separate clickable segments, and the
        // segment that is an archive carries the blue package mark — the one
        // place the UI says you have crossed into a container.
        Rectangle {
            id: addressBar

            readonly property var displayedParts: navigation.bridge.navigationPathSegments

            Layout.fillWidth: true
            Layout.preferredHeight: 28
            color: Theme.Theme.insetBg
            border.width: 1
            border.color: Theme.Theme.borderHard
            clip: true
            Accessible.name: qsTr("Current game or archive path")

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 5
                anchors.rightMargin: 3
                spacing: 2

                // Root segment: the game, or a standalone archive.
                Rectangle {
                    Layout.preferredHeight: 22
                    Layout.preferredWidth: rootRow.implicitWidth + 14
                    color: rootHover.containsMouse ? Theme.Theme.hoverChrome : "transparent"
                    RowLayout {
                        id: rootRow
                        anchors.centerIn: parent
                        spacing: 6
                        ChromeIcon {
                            visible: navigation.bridge.inArchive && !navigation.bridge.hasGame
                            kind: "package"
                            stroke: Theme.Theme.accent
                            implicitWidth: 13
                            implicitHeight: 13
                        }
                        Text {
                            text: navigation.bridge.hasWorkspace
                                ? (navigation.bridge.hasGame
                                    ? navigation.bridge.gameName
                                    : navigation.bridge.archiveRootName)
                                : qsTr("Select the GTA V folder")
                            color: navigation.bridge.inArchive && !navigation.bridge.hasGame
                                ? Theme.Theme.accent
                                : Theme.Theme.textDim
                            font.family: Theme.Theme.monoFont
                            font.pixelSize: Theme.Theme.fontSize
                        }
                    }
                    MouseArea {
                        id: rootHover
                        anchors.fill: parent
                        hoverEnabled: true
                        enabled: navigation.bridge.hasWorkspace
                        onClicked: {
                            if (navigation.bridge.hasGame && navigation.bridge.inArchive)
                                navigation.bridge.showGame()
                            else if (navigation.bridge.inArchive)
                                navigation.bridge.navigateTree("archive://.")
                            else
                                navigation.bridge.navigate(".")
                        }
                    }
                }

                // The game-relative archive path stays visible before its
                // internal directory path.
                Repeater {
                    model: addressBar.displayedParts
                    delegate: RowLayout {
                        id: segment
                        required property int index
                        required property var modelData
                        readonly property bool archiveSegment: segment.modelData.archive
                        readonly property bool last: segment.index === addressBar.displayedParts.length - 1
                        spacing: 2

                        ChromeIcon {
                            kind: "chevron"
                            stroke: Theme.Theme.textFaint
                            thickness: 2
                            implicitWidth: 12
                            implicitHeight: 12
                        }
                        Rectangle {
                            Layout.preferredHeight: 22
                            Layout.preferredWidth: segmentRow.implicitWidth + 14
                            color: segmentHover.containsMouse ? Theme.Theme.hoverChrome : "transparent"

                            RowLayout {
                                id: segmentRow
                                anchors.centerIn: parent
                                spacing: 6

                                ChromeIcon {
                                    visible: segment.archiveSegment
                                    kind: "package"
                                    stroke: Theme.Theme.accent
                                    implicitWidth: 13
                                    implicitHeight: 13
                                }

                                Text {
                                    text: segment.modelData.label
                                    color: segment.archiveSegment
                                        ? Theme.Theme.accent
                                        : (segment.last ? Theme.Theme.text : Theme.Theme.textDim)
                                    font.family: Theme.Theme.monoFont
                                    font.pixelSize: Theme.Theme.fontSize
                                }
                            }

                            MouseArea {
                                id: segmentHover
                                anchors.fill: parent
                                hoverEnabled: true
                                onClicked: navigation.bridge.navigateTree(segment.modelData.target)
                            }
                        }
                    }
                }

                Item { Layout.fillWidth: true }

                ChromeToolButton {
                    Layout.preferredWidth: 24
                    Layout.preferredHeight: 22
                    bordered: false
                    iconKind: "refresh"
                    enabled: navigation.bridge.hasWorkspace
                    Accessible.name: qsTr("Refresh")
                    onClicked: navigation.bridge.refresh()
                }
            }
        }

        FlatTextField {
            id: searchField
            Layout.preferredWidth: 216
            Layout.preferredHeight: 28
            enabled: navigation.bridge.hasWorkspace
            placeholderText: qsTr("Search this folder")
            keyCap: activeFocus || text.length > 0 ? "Esc" : "F3"
            leadingIcon: "search"
            leadingColor: text.length > 0 ? Theme.Theme.brass : Theme.Theme.textFaint
            onTextChanged: navigation.bridge.setSearch(text)
            Accessible.name: qsTr("Search current entries")
            Keys.onEscapePressed: { clear(); focus = false }
        }

        // One segmented control, not three loose buttons: the active mode is
        // sunken rather than tinted, the way a native toolbar shows it.
        Rectangle {
            Layout.preferredHeight: 28
            Layout.preferredWidth: viewModes.implicitWidth + 2
            color: "transparent"
            border.width: 1
            border.color: Theme.Theme.borderHard

            Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; anchors.margins: 1; height: 1; color: Theme.Theme.bevel }

            RowLayout {
                id: viewModes
                anchors.centerIn: parent
                spacing: 0
                ChromeToolButton {
                    Layout.preferredWidth: 30; Layout.preferredHeight: 26
                    bordered: false; sunken: true
                    iconKind: "list"; foreground: Theme.Theme.text
                    Accessible.name: qsTr("List view")
                }
                Rectangle { Layout.preferredWidth: 1; Layout.preferredHeight: 26; color: Theme.Theme.borderHard }
                ChromeToolButton {
                    Layout.preferredWidth: 30; Layout.preferredHeight: 26
                    bordered: false; iconKind: "grid"; enabled: false
                    Accessible.name: qsTr("Grid view")
                }
            }
        }

        Shortcut { sequence: "Ctrl+F"; enabled: navigation.bridge.hasWorkspace; onActivated: navigation.focusSearch() }
        Shortcut { sequence: "F3"; enabled: navigation.bridge.hasWorkspace; onActivated: navigation.focusSearch() }
        Shortcut { sequence: "Alt+Left"; onActivated: navigation.bridge.goBack() }
        Shortcut { sequence: "Alt+Right"; onActivated: navigation.bridge.goForward() }
        Shortcut { sequence: "Backspace"; onActivated: navigation.bridge.goUp() }
    }
}
