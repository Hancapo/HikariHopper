import QtQuick

/* Invisible edge grips which delegate resizing back to the window manager. */
Item {
    id: frame

    required property var targetWindow
    property int gripWidth: 5

    visible: targetWindow
        && targetWindow.visibility !== Window.Maximized
        && targetWindow.visibility !== Window.FullScreen
    Accessible.ignored: true

    MouseArea {
        anchors.left: parent.left; anchors.top: parent.top
        width: frame.gripWidth; height: frame.gripWidth
        cursorShape: Qt.SizeFDiagCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.LeftEdge | Qt.TopEdge)
    }
    MouseArea {
        anchors.right: parent.right; anchors.top: parent.top
        width: frame.gripWidth; height: frame.gripWidth
        cursorShape: Qt.SizeBDiagCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.RightEdge | Qt.TopEdge)
    }
    MouseArea {
        anchors.left: parent.left; anchors.bottom: parent.bottom
        width: frame.gripWidth; height: frame.gripWidth
        cursorShape: Qt.SizeBDiagCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.LeftEdge | Qt.BottomEdge)
    }
    MouseArea {
        anchors.right: parent.right; anchors.bottom: parent.bottom
        width: frame.gripWidth; height: frame.gripWidth
        cursorShape: Qt.SizeFDiagCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.RightEdge | Qt.BottomEdge)
    }
    MouseArea {
        anchors.left: parent.left; anchors.top: parent.top; anchors.bottom: parent.bottom
        anchors.topMargin: frame.gripWidth; anchors.bottomMargin: frame.gripWidth
        width: frame.gripWidth
        cursorShape: Qt.SizeHorCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.LeftEdge)
    }
    MouseArea {
        anchors.right: parent.right; anchors.top: parent.top; anchors.bottom: parent.bottom
        anchors.topMargin: frame.gripWidth; anchors.bottomMargin: frame.gripWidth
        width: frame.gripWidth
        cursorShape: Qt.SizeHorCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.RightEdge)
    }
    MouseArea {
        anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top
        anchors.leftMargin: frame.gripWidth; anchors.rightMargin: frame.gripWidth
        height: frame.gripWidth
        cursorShape: Qt.SizeVerCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.TopEdge)
    }
    MouseArea {
        anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom
        anchors.leftMargin: frame.gripWidth; anchors.rightMargin: frame.gripWidth
        height: frame.gripWidth
        cursorShape: Qt.SizeVerCursor
        acceptedButtons: Qt.LeftButton
        onPressed: frame.targetWindow.startSystemResize(Qt.BottomEdge)
    }
}
