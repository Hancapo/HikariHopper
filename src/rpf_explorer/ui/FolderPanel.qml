import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: folderPanel
    required property var bridge
    required property var sourceModel
    color: Theme.Theme.sidebarBg
    SplitView.minimumWidth: 210
    SplitView.preferredWidth: 247

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ListView {
                id: folderList
                anchors.fill: parent
                anchors.rightMargin: Theme.Theme.scrollbarWidth
                model: folderPanel.sourceModel
                clip: true
                reuseItems: true
                activeFocusOnTab: true
                currentIndex: folderPanel.bridge.treeFocusIndex
                Accessible.name: qsTr("Archive folders")

                Keys.onReturnPressed: if (currentIndex >= 0) folderPanel.bridge.activateTreeRow(currentIndex)
                Keys.onEnterPressed: if (currentIndex >= 0) folderPanel.bridge.activateTreeRow(currentIndex)
                Keys.onPressed: function(event) {
                    if (event.key !== Qt.Key_Up && event.key !== Qt.Key_Down)
                        return
                    const direction = event.key === Qt.Key_Up ? -1 : 1
                    const start = currentIndex >= 0 ? currentIndex : (direction > 0 ? -1 : count)
                    const target = Math.max(0, Math.min(count - 1, start + direction))
                    if (target >= 0)
                        folderPanel.bridge.focusTreeRow(target)
                    event.accepted = true
                }

                Connections {
                    target: folderPanel.bridge

                    function onTreeFocusChanged() {
                        Qt.callLater(function() {
                            if (folderList.currentIndex >= 0)
                                folderList.positionViewAtIndex(folderList.currentIndex, ListView.Contain)
                        })
                    }
                }

                ScrollBar.vertical: QuietScrollBar {
                    id: folderScrollBar
                    parent: folderList.parent
                    anchors.top: parent.top
                    anchors.right: parent.right
                    anchors.bottom: parent.bottom
                    accessibleName: qsTr("Scroll folders")
                }

                delegate: Rectangle {
                    id: folderDelegate
                    required property string label
                    required property string path
                    required property int depth
                    required property string kind
                    required property bool expanded
                    required property bool hasChildren
                    required property bool selected
                    required property int index

                    readonly property bool isArchive: folderDelegate.kind === "archive"
                    readonly property bool focused: folderList.currentIndex === folderDelegate.index
                        && !folderDelegate.selected

                    width: folderList.width
                    height: Theme.Theme.rowHeight
                    // The tree is the pane without keyboard focus, so where-you-are
                    // is a washed green with a ring rather than the solid bar the
                    // table uses. Same colour, quieter weight.
                    color: folderDelegate.selected
                        ? Theme.Theme.selectionWash
                        : folderDelegate.focused ? Theme.Theme.borderSoft
                        : rowHover.hovered ? Theme.Theme.hoverChrome : "transparent"

                    HoverHandler { id: rowHover }
                    border.width: folderDelegate.selected || folderDelegate.focused ? 1 : 0
                    border.color: folderDelegate.selected
                        ? Theme.Theme.selectionRing
                        : Theme.Theme.border

                    // Dotted guides, one per level of depth.
                    Repeater {
                        model: folderDelegate.depth
                        delegate: DottedRule {
                            required property int index
                            x: 14 + index * 15
                            anchors.top: parent.top
                            anchors.bottom: parent.bottom
                        }
                    }

                    Row {
                        z: 1
                        anchors.fill: parent
                        anchors.leftMargin: 7 + folderDelegate.depth * 15
                        spacing: 5

                        // Drawn [+] / [−] box, not a typed character.
                        Item {
                            width: 16
                            height: parent.height
                            activeFocusOnTab: folderDelegate.hasChildren
                            Accessible.role: Accessible.Button
                            Accessible.ignored: !folderDelegate.hasChildren
                            Accessible.name: folderDelegate.expanded
                                ? qsTr("Collapse %1").arg(folderDelegate.label)
                                : qsTr("Expand %1").arg(folderDelegate.label)
                            readonly property color toggleInk: toggleHover.hovered
                                ? Theme.Theme.text
                                : Theme.Theme.textDim

                            HoverHandler { id: toggleHover; enabled: folderDelegate.hasChildren }

                            Rectangle {
                                id: toggleBox
                                anchors.centerIn: parent
                                visible: folderDelegate.hasChildren
                                width: 9
                                height: 9
                                color: folderPanel.color
                                border.width: 1
                                border.color: parent.toggleInk
                                Rectangle { anchors.centerIn: parent; width: 5; height: 1; color: toggleBox.border.color }
                                Rectangle { anchors.centerIn: parent; visible: !folderDelegate.expanded; width: 1; height: 5; color: toggleBox.border.color }
                            }
                            MouseArea {
                                anchors.fill: parent
                                enabled: folderDelegate.hasChildren
                                cursorShape: folderDelegate.hasChildren
                                    ? Qt.PointingHandCursor
                                    : Qt.ArrowCursor
                                onClicked: folderPanel.bridge.toggleTreeNode(folderDelegate.path)
                            }
                            Keys.onSpacePressed: if (folderDelegate.hasChildren) folderPanel.bridge.toggleTreeNode(folderDelegate.path)
                            Keys.onReturnPressed: if (folderDelegate.hasChildren) folderPanel.bridge.toggleTreeNode(folderDelegate.path)
                        }

                        FileGlyph {
                            anchors.verticalCenter: parent.verticalCenter
                            kind: folderDelegate.isArchive ? "archive" : "folder"
                        }

                        Text {
                            height: parent.height
                            width: Math.max(0, folderDelegate.width - x - 8)
                            text: folderDelegate.label
                            color: folderDelegate.selected ? Theme.Theme.text : Theme.Theme.textRow
                            font.family: Theme.Theme.monoFont
                            font.pixelSize: Theme.Theme.fontSize
                            font.bold: folderDelegate.kind === "root" || folderDelegate.isArchive
                            verticalAlignment: Text.AlignVCenter
                            elide: Text.ElideRight
                        }
                    }

                    MouseArea {
                        anchors.fill: parent
                        onClicked: {
                            folderPanel.bridge.focusTreePath(folderDelegate.path)
                            folderList.forceActiveFocus()
                        }
                        onDoubleClicked: folderPanel.bridge.activateTreeRow(folderDelegate.index)
                    }
                }
            }
        }
    }
}
