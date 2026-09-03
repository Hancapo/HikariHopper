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

    function openContextMenu(source, localX, localY) {
        const point = source.mapToItem(view, localX, localY)
        explorerContextMenu.x = point.x
        explorerContextMenu.y = point.y
        explorerContextMenu.open()
    }

    function openCreateDialog(kind, suggestedName, sourcePath) {
        createDialogLoader.creationKind = kind
        createDialogLoader.suggestedName = suggestedName
        createDialogLoader.sourcePath = sourcePath
        createDialogLoader.active = true
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
            onContextMenuRequested: function(source, x, y) {
                view.openContextMenu(source, x, y)
            }
        }
    }

    Component {
        id: gridComponent
        EntryGrid {
            bridge: view.bridge
            sourceModel: view.entryModel
            onContextMenuRequested: function(source, x, y) {
                view.openContextMenu(source, x, y)
            }
        }
    }

    ExplorerContextMenu {
        id: explorerContextMenu
        objectName: "explorerContextMenu"
        bridge: view.bridge
        onCreateRequested: function(kind, suggestedName, sourcePath) {
            view.openCreateDialog(kind, suggestedName, sourcePath)
        }
    }

    Loader {
        id: createDialogLoader

        property string creationKind: ""
        property string suggestedName: ""
        property string sourcePath: ""

        active: false
        sourceComponent: createDialogComponent
    }

    Component {
        id: createDialogComponent
        ExplorerCreateDialog {
            objectName: "explorerCreateDialog"
            bridge: view.bridge
            Component.onCompleted: begin(
                createDialogLoader.creationKind,
                createDialogLoader.suggestedName,
                createDialogLoader.sourcePath
            )
            onClosed: Qt.callLater(function() {
                createDialogLoader.active = false
            })
        }
    }
}
