import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQml.Models
import "theme" as Theme

ApplicationWindow {
    id: window
    visible: true
    width: 1360
    height: 840
    minimumWidth: 940
    minimumHeight: 580
    title: tabs.activeBridge.hasWorkspace
        ? qsTr("HikariHopper — %1").arg(tabs.activeBridge.tabTitle)
        : qsTr("HikariHopper — RPF Explorer")
    color: Theme.Theme.appBg
    flags: Qt.Window | Qt.FramelessWindowHint
    required property var tabs

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        WindowTitleBar {
            Layout.fillWidth: true
            targetWindow: window
            titleText: window.title
        }

        ExplorerScreen {
            Layout.fillWidth: true
            Layout.fillHeight: true
            tabs: window.tabs
        }
    }

    Instantiator {
        model: window.tabs
        delegate: TextureDictionaryWindow {
            id: textureWindow
            required property var textureViewer
            bridge: textureWindow.textureViewer
        }
    }

    // Blue piping around the whole window, the way the reference uniform runs
    // its trim along every edge. Drawn on top so no panel seam covers it.
    Rectangle {
        anchors.fill: parent
        z: 100
        color: "transparent"
        border.width: 1
        border.color: Theme.Theme.borderAccent
    }

    WindowResizeFrame {
        anchors.fill: parent
        z: 200
        targetWindow: window
    }
}
