import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "grid_math.js" as GridMath
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: gridPanel

    required property var bridge
    required property var sourceModel

    color: Theme.Theme.panelBg
    SplitView.fillWidth: true

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        EntryGridHeader {
            Layout.fillWidth: true
            bridge: gridPanel.bridge
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            GridView {
                id: entryGrid
                objectName: "entryGrid"

                readonly property int columnsVisible: Math.max(
                    1,
                    Math.floor((width - leftMargin - rightMargin) / cellWidth)
                )

                anchors.fill: parent
                anchors.rightMargin: Theme.Theme.scrollbarWidth
                model: gridPanel.sourceModel
                cellWidth: Theme.Theme.gridCellWidth
                cellHeight: Theme.Theme.gridCellHeight
                leftMargin: Theme.Theme.gridContentInset
                rightMargin: Theme.Theme.gridContentInset
                topMargin: Theme.Theme.gridContentInset
                bottomMargin: Theme.Theme.gridContentInset
                clip: true
                reuseItems: true
                focus: true
                activeFocusOnTab: true
                highlightFollowsCurrentItem: false
                Accessible.name: qsTr("Archive entries grid")

                ScrollBar.vertical: QuietScrollBar {
                    parent: entryGrid.parent
                    anchors.top: parent.top
                    anchors.right: parent.right
                    anchors.bottom: parent.bottom
                    accessibleName: qsTr("Scroll grid entries")
                }

                delegate: EntryGridCell {
                    bridge: gridPanel.bridge
                }

                currentIndex: gridPanel.bridge.selectedIndex
                onCountChanged: positionViewAtBeginning()
                Keys.onReturnPressed: if (currentIndex >= 0) gridPanel.bridge.activateEntry(currentIndex)
                Keys.onEnterPressed: if (currentIndex >= 0) gridPanel.bridge.activateEntry(currentIndex)
                Keys.onPressed: function(event) {
                    if (event.key === Qt.Key_A && event.modifiers & Qt.ControlModifier) {
                        gridPanel.bridge.selectAllEntries()
                        event.accepted = true
                        return
                    }

                    let direction = ""
                    if (event.key === Qt.Key_Left)
                        direction = "left"
                    else if (event.key === Qt.Key_Right)
                        direction = "right"
                    else if (event.key === Qt.Key_Up)
                        direction = "up"
                    else if (event.key === Qt.Key_Down)
                        direction = "down"
                    if (direction === "")
                        return

                    const target = GridMath.navigationTarget(
                        currentIndex,
                        count,
                        columnsVisible,
                        direction
                    )
                    if (target >= 0) {
                        gridPanel.bridge.selectEntry(target, event.modifiers & Qt.ShiftModifier)
                        positionViewAtIndex(target, GridView.Contain)
                    }
                    event.accepted = true
                }
            }

            Text {
                anchors.centerIn: parent
                visible: entryGrid.count === 0
                text: qsTr("This folder is empty")
                color: Theme.Theme.textDim
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.fontSize
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
                visible: entryGrid.count > 0
                acceptedButtons: Qt.LeftButton
                Accessible.ignored: true

                function indexAtPoint(x, y) {
                    return entryGrid.indexAt(
                        x + entryGrid.contentX,
                        y + entryGrid.contentY
                    )
                }

                function updateSelection() {
                    const left = Math.min(originX, pointerX) + entryGrid.contentX
                    const right = Math.max(originX, pointerX) + entryGrid.contentX
                    const top = Math.min(originY, pointerY) + entryGrid.contentY
                    const bottom = Math.max(originY, pointerY) + entryGrid.contentY
                    gridPanel.bridge.updateMarqueeRows(GridMath.indicesInRect(
                        entryGrid.count,
                        entryGrid.columnsVisible,
                        entryGrid.cellWidth,
                        entryGrid.cellHeight,
                        entryGrid.leftMargin,
                        entryGrid.topMargin,
                        left,
                        right,
                        top,
                        bottom
                    ))
                }

                onPressed: function(event) {
                    if (indexAtPoint(event.x, event.y) >= 0) {
                        event.accepted = false
                        return
                    }
                    originX = GridMath.clamp(event.x, 0, width)
                    originY = GridMath.clamp(event.y, 0, height)
                    pointerX = originX
                    pointerY = originY
                    pressModifiers = event.modifiers
                    tracking = true
                    marqueeActive = false
                    entryGrid.forceActiveFocus()
                }

                onPositionChanged: function(event) {
                    if (!tracking)
                        return
                    pointerX = GridMath.clamp(event.x, 0, width)
                    pointerY = GridMath.clamp(event.y, 0, height)
                    const distanceX = pointerX - originX
                    const distanceY = pointerY - originY
                    const threshold = Application.styleHints.startDragDistance
                    if (!marqueeActive
                            && distanceX * distanceX + distanceY * distanceY >= threshold * threshold) {
                        marqueeActive = true
                        gridPanel.bridge.beginMarqueeSelection(pressModifiers)
                    }
                    if (marqueeActive)
                        updateSelection()
                }

                onReleased: function(event) {
                    if (!tracking)
                        return
                    if (marqueeActive) {
                        pointerX = GridMath.clamp(event.x, 0, width)
                        pointerY = GridMath.clamp(event.y, 0, height)
                        updateSelection()
                        gridPanel.bridge.endMarqueeSelection()
                    } else if (!(pressModifiers & (Qt.ControlModifier | Qt.ShiftModifier))) {
                        gridPanel.bridge.clearEntrySelection()
                    }
                    tracking = false
                    marqueeActive = false
                }

                onCanceled: {
                    if (marqueeActive)
                        gridPanel.bridge.endMarqueeSelection()
                    tracking = false
                    marqueeActive = false
                }
            }
        }
    }
}
