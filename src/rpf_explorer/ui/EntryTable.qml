import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "entry_text.js" as EntryText
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: tablePanel
    required property var bridge
    required property var sourceModel

    readonly property int nameWidth: Math.max(300, Math.min(500, Math.round(width * 0.46)))
    readonly property int typeWidth: 190
    readonly property int sizeWidth: 124
    readonly property int dataBlockWidth: nameWidth + typeWidth + sizeWidth

    color: Theme.Theme.panelBg
    SplitView.fillWidth: true

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: Theme.Theme.headerHeight
            Layout.minimumHeight: Theme.Theme.headerHeight
            Layout.maximumHeight: Theme.Theme.headerHeight
            color: Theme.Theme.headerBg

            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.top: parent.top
                height: 1
                color: Theme.Theme.bevel
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                height: 1
                color: Theme.Theme.borderHard
            }

            RowLayout {
                anchors.fill: parent
                anchors.rightMargin: Theme.Theme.scrollbarWidth
                spacing: 0

                EntryTableHeaderCell {
                    Layout.preferredWidth: tablePanel.nameWidth
                    Layout.fillHeight: true
                    text: qsTr("NAME")
                    leftPadding: 10
                    sortActive: tablePanel.bridge.sortColumn === "name"
                    sortAscending: tablePanel.bridge.sortAscending
                    onClicked: tablePanel.bridge.sortEntries("name")
                }

                EntryTableHeaderCell {
                    Layout.preferredWidth: tablePanel.typeWidth
                    Layout.fillHeight: true
                    text: qsTr("TYPE")
                    leftPadding: 10
                    sortActive: tablePanel.bridge.sortColumn === "type"
                    sortAscending: tablePanel.bridge.sortAscending
                    onClicked: tablePanel.bridge.sortEntries("type")
                }

                EntryTableHeaderCell {
                    Layout.preferredWidth: tablePanel.sizeWidth
                    Layout.fillHeight: true
                    text: qsTr("SIZE")
                    alignRight: true
                    rightPadding: 12
                    sortActive: tablePanel.bridge.sortColumn === "size"
                    sortAscending: tablePanel.bridge.sortAscending
                    onClicked: tablePanel.bridge.sortEntries("size")
                }

                Item {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                }
            }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ListView {
                id: entryTable
                objectName: "entryTable"
                anchors.fill: parent
                anchors.rightMargin: Theme.Theme.scrollbarWidth
                model: tablePanel.sourceModel
                clip: true
                reuseItems: true
                focus: true
                activeFocusOnTab: true
                Accessible.name: qsTr("Archive entries")

                ScrollBar.vertical: QuietScrollBar {
                    id: entryScrollBar
                    parent: entryTable.parent
                    anchors.top: parent.top
                    anchors.right: parent.right
                    anchors.bottom: parent.bottom
                    accessibleName: qsTr("Scroll entries")
                }

                delegate: Rectangle {
                    id: entryDelegate
                    required property string name
                    required property string path
                    required property string kind
                    required property string sizeLabel
                    required property int childCount
                    required property bool isDirectory
                    required property bool selected
                    required property int index

                    width: Math.min(tablePanel.dataBlockWidth, entryTable.width)
                    height: Theme.Theme.rowHeight
                    color: entryDelegate.selected
                        ? Theme.Theme.selection
                        : mouse.containsMouse ? Theme.Theme.hoverBg : "transparent"

                    RowLayout {
                        anchors.fill: parent
                        spacing: 0

                        Item {
                            Layout.preferredWidth: 36
                            Layout.fillHeight: true

                            FileGlyph {
                                anchors.centerIn: parent
                                kind: entryDelegate.isDirectory
                                    ? "folder"
                                    : (entryDelegate.kind.indexOf("Package") >= 0 ? "archive" : "file")
                                fileKind: entryDelegate.kind
                                selected: entryDelegate.selected
                            }
                        }

                        Item {
                            id: nameCell
                            Layout.preferredWidth: tablePanel.nameWidth - 36
                            Layout.fillHeight: true

                            Text {
                                id: entryName
                                anchors.left: parent.left
                                anchors.verticalCenter: parent.verticalCenter
                                width: Math.min(
                                    implicitWidth,
                                    parent.width - (childCountLabel.visible ? childCountLabel.width + 14 : 8)
                                )
                                text: EntryText.markMatch(
                                    entryDelegate.name,
                                    tablePanel.bridge.searchQuery,
                                    entryDelegate.selected
                                        ? Theme.Theme.selectionText
                                        : Theme.Theme.brass
                                )
                                textFormat: Text.StyledText
                                color: entryDelegate.selected
                                    ? Theme.Theme.selectionText
                                    : Theme.Theme.textRow
                                font.family: Theme.Theme.monoFont
                                font.pixelSize: Theme.Theme.fontSize
                                elide: Text.ElideRight
                            }

                            Text {
                                id: childCountLabel
                                anchors.left: entryName.right
                                anchors.leftMargin: 10
                                anchors.verticalCenter: parent.verticalCenter
                                visible: entryDelegate.childCount > 0
                                text: qsTr("%1 items").arg(entryDelegate.childCount)
                                color: entryDelegate.selected
                                    ? Theme.Theme.selectionFaint
                                    : Theme.Theme.textFaint
                                font.family: Theme.Theme.monoFont
                                font.pixelSize: Theme.Theme.smallFontSize
                            }
                        }

                        Item {
                            Layout.preferredWidth: tablePanel.typeWidth
                            Layout.fillHeight: true

                            Text {
                                anchors.fill: parent
                                leftPadding: 10
                                text: entryDelegate.kind
                                color: entryDelegate.selected
                                    ? Theme.Theme.selectionInk
                                    : Theme.Theme.textDim
                                font.family: Theme.Theme.uiFont
                                font.pixelSize: Theme.Theme.fontSize
                                verticalAlignment: Text.AlignVCenter
                                elide: Text.ElideRight
                            }
                        }

                        Item {
                            Layout.preferredWidth: tablePanel.sizeWidth
                            Layout.fillHeight: true

                            Text {
                                anchors.fill: parent
                                rightPadding: 12
                                text: entryDelegate.isDirectory ? "—" : entryDelegate.sizeLabel
                                color: entryDelegate.selected
                                    ? Theme.Theme.selectionText
                                    : Theme.Theme.textRow
                                font.family: Theme.Theme.monoFont
                                font.pixelSize: Theme.Theme.fontSize
                                horizontalAlignment: Text.AlignRight
                                verticalAlignment: Text.AlignVCenter
                            }
                        }
                    }

                    EntryPointerArea {
                        id: mouse
                        bridge: tablePanel.bridge
                        entryIndex: entryDelegate.index
                        entrySelected: entryDelegate.selected
                        focusTarget: entryTable
                        accessibleName: entryDelegate.name
                    }
                }

                currentIndex: tablePanel.bridge.selectedIndex
                onCountChanged: positionViewAtBeginning()
                Keys.onReturnPressed: if (currentIndex >= 0) tablePanel.bridge.activateEntry(currentIndex)
                Keys.onEnterPressed: if (currentIndex >= 0) tablePanel.bridge.activateEntry(currentIndex)
                Keys.onPressed: function(event) {
                    if (event.key === Qt.Key_A && event.modifiers & Qt.ControlModifier) {
                        tablePanel.bridge.selectAllEntries();
                        event.accepted = true;
                    } else if (event.key === Qt.Key_Up || event.key === Qt.Key_Down) {
                        const direction = event.key === Qt.Key_Up ? -1 : 1;
                        const start = currentIndex >= 0 ? currentIndex : (direction > 0 ? -1 : count);
                        const target = Math.max(0, Math.min(count - 1, start + direction));
                        if (target >= 0)
                            tablePanel.bridge.selectEntry(target, event.modifiers & Qt.ShiftModifier);
                        event.accepted = true;
                    }
                }
            }

            Column {
                anchors.centerIn: entryTable
                spacing: 12
                visible: entryTable.count === 0

                Text {
                    anchors.horizontalCenter: parent.horizontalCenter
                    text: tablePanel.bridge.hasWorkspace
                        ? qsTr("This folder is empty")
                        : qsTr("Configure a GTA V installation to begin")
                    color: Theme.Theme.textDim
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: Theme.Theme.fontSize
                }

                ChromeToolButton {
                    anchors.horizontalCenter: parent.horizontalCenter
                    width: 190
                    height: 32
                    visible: !tablePanel.bridge.hasWorkspace
                    primary: true
                    text: qsTr("OPEN CONFIGURED GAME")
                    onClicked: tablePanel.bridge.openConfiguredGame("")
                }
            }

            SelectionMarquee {
                x: Math.min(marqueeArea.originX, marqueeArea.pointerX)
                y: Math.min(marqueeArea.originY, marqueeArea.pointerY)
                width: Math.abs(marqueeArea.pointerX - marqueeArea.originX)
                height: Math.abs(marqueeArea.pointerY - marqueeArea.originY)
                z: 20
                visible: marqueeArea.marqueeActive
            }

            MouseArea {
                id: marqueeArea
                property real originX: 0
                property real originY: 0
                property real pointerX: 0
                property real pointerY: 0
                property bool tracking: false
                property bool marqueeActive: false
                property int pressModifiers: Qt.NoModifier

                anchors.fill: parent
                anchors.rightMargin: Theme.Theme.scrollbarWidth
                z: 3
                visible: entryTable.count > 0
                acceptedButtons: Qt.LeftButton
                Accessible.ignored: true

                function rowAt(x, y) {
                    return entryTable.indexAt(
                        Math.min(x, tablePanel.dataBlockWidth - 1) + entryTable.contentX,
                        y + entryTable.contentY
                    );
                }

                function isBackground(x, y) {
                    return x >= tablePanel.dataBlockWidth || rowAt(x, y) < 0;
                }

                function clampX(value) {
                    return Math.max(0, Math.min(width, value));
                }

                function clampY(value) {
                    return Math.max(0, Math.min(height, value));
                }

                function updateSelection() {
                    const left = Math.min(originX, pointerX);
                    const right = Math.max(originX, pointerX);
                    const top = Math.min(originY, pointerY);
                    const bottom = Math.max(originY, pointerY);
                    const contentBottom = Math.max(
                        0,
                        Math.min(height, entryTable.contentHeight - entryTable.contentY)
                    );
                    if (left >= tablePanel.dataBlockWidth || right <= 0 || top >= contentBottom) {
                        tablePanel.bridge.updateMarqueeSelection(-1, -1);
                        return;
                    }
                    const sampleBottom = Math.max(top, Math.min(bottom, contentBottom) - 1);
                    const first = rowAt(1, top);
                    const last = rowAt(1, sampleBottom);
                    tablePanel.bridge.updateMarqueeSelection(first, last);
                }

                onPressed: function(event) {
                    if (!isBackground(event.x, event.y)) {
                        event.accepted = false;
                        return;
                    }
                    originX = clampX(event.x);
                    originY = clampY(event.y);
                    pointerX = originX;
                    pointerY = originY;
                    pressModifiers = event.modifiers;
                    tracking = true;
                    marqueeActive = false;
                    entryTable.forceActiveFocus();
                }

                onPositionChanged: function(event) {
                    if (!tracking)
                        return;
                    pointerX = clampX(event.x);
                    pointerY = clampY(event.y);
                    const distanceX = pointerX - originX;
                    const distanceY = pointerY - originY;
                    const threshold = Application.styleHints.startDragDistance;
                    if (!marqueeActive
                            && distanceX * distanceX + distanceY * distanceY >= threshold * threshold) {
                        marqueeActive = true;
                        tablePanel.bridge.beginMarqueeSelection(pressModifiers);
                    }
                    if (marqueeActive)
                        updateSelection();
                }

                onReleased: function(event) {
                    if (!tracking)
                        return;
                    if (marqueeActive) {
                        pointerX = clampX(event.x);
                        pointerY = clampY(event.y);
                        updateSelection();
                        tablePanel.bridge.endMarqueeSelection();
                    } else if (!(pressModifiers & (Qt.ControlModifier | Qt.ShiftModifier))) {
                        tablePanel.bridge.clearEntrySelection();
                    }
                    tracking = false;
                    marqueeActive = false;
                }

                onCanceled: {
                    if (marqueeActive)
                        tablePanel.bridge.endMarqueeSelection();
                    tracking = false;
                    marqueeActive = false;
                }
            }
        }
    }
}
