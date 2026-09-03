import QtQuick
import QtQuick.Layouts
import "theme" as Theme

/* Application-owned decoration backed by native window operations. */
Rectangle {
    id: titleBar

    required property var targetWindow
    property string titleText: "HikariHopper"
    property bool showMinimize: true
    property bool showMaximize: true
    property int controlWidth: 34

    readonly property bool maximized: targetWindow
        && targetWindow.visibility === Window.Maximized

    implicitHeight: Theme.Theme.windowTitleHeight
    color: Theme.Theme.chromeRaised

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
        spacing: 0

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            DragHandler {
                target: null
                acceptedButtons: Qt.LeftButton
                onActiveChanged: if (active) titleBar.targetWindow.startSystemMove()
            }

            TapHandler {
                acceptedButtons: Qt.LeftButton
                onDoubleTapped: {
                    if (!titleBar.showMaximize)
                        return
                    if (titleBar.maximized)
                        titleBar.targetWindow.showNormal()
                    else
                        titleBar.targetWindow.showMaximized()
                }
            }

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 9
                anchors.rightMargin: 8
                spacing: 8

                Rectangle {
                    Layout.preferredWidth: 12
                    Layout.preferredHeight: 12
                    color: "transparent"
                    border.width: 1
                    border.color: Theme.Theme.brass

                    Rectangle {
                        anchors.left: parent.left
                        anchors.top: parent.top
                        anchors.margins: 2
                        width: 3
                        height: 3
                        color: Theme.Theme.brass
                    }
                }

                Text {
                    Layout.fillWidth: true
                    text: titleBar.titleText
                    color: Theme.Theme.textRow
                    font.family: Theme.Theme.uiFont
                    font.pixelSize: Theme.Theme.smallFontSize
                    verticalAlignment: Text.AlignVCenter
                    elide: Text.ElideRight
                }
            }
        }

        WindowControlButton {
            visible: titleBar.showMinimize
            Layout.preferredWidth: visible ? titleBar.controlWidth : 0
            Layout.fillHeight: true
            kind: "minimize"
            Accessible.name: qsTr("Minimize window")
            onClicked: titleBar.targetWindow.showMinimized()
        }

        WindowControlButton {
            visible: titleBar.showMaximize
            Layout.preferredWidth: visible ? titleBar.controlWidth : 0
            Layout.fillHeight: true
            kind: titleBar.maximized ? "restore" : "maximize"
            Accessible.name: titleBar.maximized
                ? qsTr("Restore window")
                : qsTr("Maximize window")
            onClicked: {
                if (titleBar.maximized)
                    titleBar.targetWindow.showNormal()
                else
                    titleBar.targetWindow.showMaximized()
            }
        }

        WindowControlButton {
            Layout.preferredWidth: titleBar.controlWidth
            Layout.fillHeight: true
            kind: "close"
            Accessible.name: qsTr("Close window")
            onClicked: titleBar.targetWindow.close()
        }
    }
}
