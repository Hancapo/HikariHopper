import QtQuick

MouseArea {
    id: pointer

    required property var bridge
    required property int entryIndex
    required property bool entrySelected
    required property Item focusTarget
    property string accessibleName: ""

    signal contextMenuRequested(Item source, real x, real y)

    property real pressX: 0
    property real pressY: 0
    property bool dragStarted: false
    property bool selectionChangedOnPress: false

    anchors.fill: parent
    acceptedButtons: Qt.LeftButton | Qt.RightButton
    hoverEnabled: true
    preventStealing: true
    Accessible.name: pointer.accessibleName

    onPressed: function(event) {
        if (event.button === Qt.RightButton) {
            if (!pointer.entrySelected)
                pointer.bridge.selectEntry(pointer.entryIndex, event.modifiers)
            pointer.focusTarget.forceActiveFocus()
            pointer.contextMenuRequested(pointer, event.x, event.y)
            return
        }
        pressX = event.x
        pressY = event.y
        dragStarted = false
        selectionChangedOnPress = !pointer.entrySelected
        if (selectionChangedOnPress)
            pointer.bridge.selectEntry(pointer.entryIndex, event.modifiers)
        pointer.focusTarget.forceActiveFocus()
    }

    onPositionChanged: function(event) {
        if (!(pressedButtons & Qt.LeftButton))
            return
        const distanceX = event.x - pressX
        const distanceY = event.y - pressY
        const threshold = Application.styleHints.startDragDistance
        if (!dragStarted && pressed
                && distanceX * distanceX + distanceY * distanceY >= threshold * threshold) {
            dragStarted = true
            pointer.bridge.startEntryDrag(pointer.entryIndex)
        }
    }

    onDoubleClicked: function(event) {
        if (event.button === Qt.LeftButton && !dragStarted)
            pointer.bridge.activateEntry(pointer.entryIndex)
    }

    onClicked: function(event) {
        if (event.button === Qt.LeftButton && !dragStarted && !selectionChangedOnPress)
            pointer.bridge.selectEntry(pointer.entryIndex, event.modifiers)
    }

    onCanceled: dragStarted = false
}
