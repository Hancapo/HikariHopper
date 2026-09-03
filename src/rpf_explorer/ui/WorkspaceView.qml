import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

pragma ComponentBehavior: Bound

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
            handle: SplitViewHandle { }

            FolderPanel {
                visible: view.bridge.foldersVisible
                bridge: view.bridge
                sourceModel: view.treeModel
            }

            Loader {
                SplitView.fillWidth: true
                sourceComponent: view.bridge.viewMode === "grid"
                    ? gridComponent
                    : listComponent
            }
        }

        StatusBar { bridge: view.bridge }
    }

    Component {
        id: listComponent
        EntryTable {
            bridge: view.bridge
            sourceModel: view.entryModel
        }
    }

    Component {
        id: gridComponent
        EntryGrid {
            bridge: view.bridge
            sourceModel: view.entryModel
        }
    }
}
