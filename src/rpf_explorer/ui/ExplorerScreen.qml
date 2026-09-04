import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: screen
    required property var tabs
    readonly property var bridge: tabs.activeBridge
    color: Theme.Theme.appBg

    ColumnLayout {
        anchors.fill: parent
        spacing: 0
        MenuRow {
            tabs: screen.tabs
            onSearchRequested: screen.bridge.requestSearchFocus()
        }
        TabStrip { tabs: screen.tabs }
        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            Loader {
                id: workspaceLoader
                anchors.fill: parent
                sourceComponent: screen.bridge.hasWorkspace
                    ? workspaceComponent
                    : startComponent
            }

            GameLoadingView {
                anchors.fill: parent
                bridge: screen.bridge
            }
        }
    }

    Component {
        id: workspaceComponent
        WorkspaceView {
            bridge: screen.bridge
            entryModel: screen.tabs.activeEntryModel
            treeModel: screen.tabs.activeTreeModel
        }
    }

    Component {
        id: startComponent
        StartPage {
            tabs: screen.tabs
            bridge: screen.bridge
        }
    }
}
