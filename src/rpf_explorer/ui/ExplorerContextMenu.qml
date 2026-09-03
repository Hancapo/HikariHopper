import QtQuick

RetroMenu {
    id: contextMenu

    required property var bridge

    signal createRequested(string kind, string suggestedName, string sourcePath)

    function requestCreation(kind, suggestedName, sourcePath) {
        Qt.callLater(function() {
            contextMenu.createRequested(kind, suggestedName, sourcePath)
        })
    }

    function chooseFolderSource() {
        const source = contextMenu.bridge.chooseRpfFolderSource()
        if (source.path !== "")
            requestCreation("folder-rpf", source.suggestedName, source.path)
    }

    function chooseZipSource() {
        const source = contextMenu.bridge.chooseRpfZipSource()
        if (source.path !== "")
            requestCreation("zip-rpf", source.suggestedName, source.path)
    }

    RetroMenuItem {
        text: qsTr("Open")
        enabled: contextMenu.bridge.selectionCount === 1
            && !contextMenu.bridge.entryOperationBusy
        onTriggered: contextMenu.bridge.activateEntry(contextMenu.bridge.selectedIndex)
    }

    RetroMenu {
        objectName: "newEntryMenu"
        title: qsTr("New")
        icon.name: "plus"
        enabled: !contextMenu.bridge.entryOperationBusy

        RetroMenuItem {
            text: qsTr("Folder")
            iconName: "folder"
            onTriggered: contextMenu.requestCreation("folder", qsTr("New folder"), "")
        }
        RetroMenuItem {
            text: qsTr("Empty RPF archive…")
            iconName: "package-plus"
            onTriggered: contextMenu.requestCreation("empty-rpf", qsTr("new_archive.rpf"), "")
        }
        RetroMenuItem {
            text: qsTr("RPF from folder…")
            iconName: "folder-input"
            onTriggered: Qt.callLater(contextMenu.chooseFolderSource)
        }
        RetroMenuItem {
            text: qsTr("RPF from ZIP…")
            iconName: "file-archive"
            onTriggered: Qt.callLater(contextMenu.chooseZipSource)
        }
    }

    RetroMenuSeparator { }

    RetroMenuItem {
        text: qsTr("Paste")
        shortcutText: "Ctrl+V"
        enabled: false
    }
    RetroMenuItem {
        objectName: "deleteMenuItem"
        text: qsTr("Delete")
        shortcutText: "Del"
        iconName: "trash-2"
        enabled: contextMenu.bridge.selectionDeletable
            && !contextMenu.bridge.entryOperationBusy
        onTriggered: Qt.callLater(contextMenu.bridge.requestDeleteSelection)
    }

    RetroMenuSeparator { }

    RetroMenuItem {
        text: qsTr("Refresh")
        shortcutText: "F5"
        iconName: "rotate-cw"
        enabled: !contextMenu.bridge.entryOperationBusy
        onTriggered: contextMenu.bridge.refresh()
    }
}
