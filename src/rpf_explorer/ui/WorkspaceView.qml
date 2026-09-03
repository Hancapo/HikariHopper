import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

Item {
    id: view
    required property var bridge
    required property var entryModel
    required property var treeModel

    function focusSearch() {
        navigation.focusSearch()
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        NavigationBar {
            id: navigation
            bridge: view.bridge
        }

        SplitView {
            Layout.fillWidth: true
            Layout.fillHeight: true
            orientation: Qt.Horizontal
            handle: Rectangle {
                implicitWidth: 1
                color: Theme.Theme.border
            }

            FolderPanel {
                visible: view.bridge.foldersVisible
                bridge: view.bridge
                sourceModel: view.treeModel
            }

            EntryTable {
                bridge: view.bridge
                sourceModel: view.entryModel
            }
        }

        StatusBar { bridge: view.bridge }
    }
}
