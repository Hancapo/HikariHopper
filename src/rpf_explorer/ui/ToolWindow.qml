import QtQuick
import QtQuick.Layouts
import "theme" as Theme

/*
 * The chassis every secondary window wears.
 *
 * These windows are frameless: the platform decoration is dropped and the panel
 * header strip becomes the title bar, so a Settings or About window is built out
 * of the same parts as a docked panel instead of arriving in whatever the OS
 * paints. Dragging that strip performs a real system move, so snapping and
 * multi-monitor behaviour stay native.
 *
 * Put content inside as ordinary children; they are parented into the body.
 */
Window {
    id: root

    property string heading: ""
    property int bodyMargin: 18
    default property alias content: body.data

    color: Theme.Theme.appBg
    flags: Qt.Dialog | Qt.FramelessWindowHint
    modality: Qt.NonModal

    // Painted explicitly rather than leaning on Window.color, so the surface is
    // there whatever the platform does with the window background.
    Rectangle {
        anchors.fill: parent
        color: Theme.Theme.appBg
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 1
        spacing: 0

        WindowTitleBar {
            Layout.fillWidth: true
            Layout.preferredHeight: Theme.Theme.headerHeight
            targetWindow: root
            titleText: root.heading
            showMinimize: false
            showMaximize: false
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            Item {
                id: body
                anchors.fill: parent
                anchors.margins: root.bodyMargin
            }
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 44
            color: Theme.Theme.chromeBg

            Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; height: 1; color: Theme.Theme.border }

            ChromeToolButton {
                anchors.right: parent.right
                anchors.rightMargin: 12
                anchors.verticalCenter: parent.verticalCenter
                width: 92
                height: 28
                raised: true
                text: qsTr("Close")
                onClicked: root.close()
            }
        }
    }

    // The blue piping every window that owns its frame carries. On a frameless
    // window it is also the only thing separating it from what is behind.
    Rectangle {
        anchors.fill: parent
        z: 100
        color: "transparent"
        border.width: 1
        border.color: Theme.Theme.borderAccent
    }

    Shortcut { sequence: "Esc"; onActivated: root.close() }
}
