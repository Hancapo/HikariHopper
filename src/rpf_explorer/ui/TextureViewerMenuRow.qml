import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: menuRow

    required property var bridge
    required property var viewerWindow

    implicitHeight: Theme.Theme.menuHeight
    color: Theme.Theme.chromeBg

    readonly property int popupOpticalOffset: 0

    function openPopup(menu, button) {
        const origin = button.mapToItem(menuRow, 0, 0)
        menu.x = origin.x + menuRow.popupOpticalOffset
        menu.y = menuRow.height - 1
        menu.open()
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
        anchors.leftMargin: 1
        spacing: 0

        MenuBarButton {
            id: fileButton
            text: qsTr("File")
            menuOpen: fileMenu.visible
            onClicked: menuRow.openPopup(fileMenu, fileButton)
        }

        MenuBarButton {
            id: editButton
            text: qsTr("Edit")
            menuOpen: editMenu.visible
            onClicked: menuRow.openPopup(editMenu, editButton)
        }

        MenuBarButton {
            id: helpButton
            text: qsTr("Help")
            menuOpen: helpMenu.visible
            onClicked: menuRow.openPopup(helpMenu, helpButton)
        }

        Item { Layout.fillWidth: true }
    }

    RetroMenu {
        id: editMenu
        connectionWidth: editButton.width
        RetroMenuItem {
            text: menuRow.bridge.undoLabel === ""
                ? qsTr("Undo")
                : qsTr("Undo %1").arg(menuRow.bridge.undoLabel)
            shortcutText: "Ctrl+Z"
            enabled: menuRow.bridge.canUndo
            onTriggered: menuRow.bridge.undo()
        }
    }

    RetroMenu {
        id: fileMenu
        connectionWidth: fileButton.width
        RetroMenuItem {
            text: qsTr("Save YTD")
            shortcutText: "Ctrl+S"
            enabled: menuRow.bridge.textureCount > 0 && menuRow.bridge.canSaveSource
            onTriggered: menuRow.bridge.saveYtd()
        }
        RetroMenuItem {
            text: qsTr("Save YTD as…")
            shortcutText: "Ctrl+Shift+S"
            enabled: menuRow.bridge.textureCount > 0
            onTriggered: menuRow.bridge.saveYtdAs()
        }
        RetroMenuSeparator { }
        RetroMenuItem {
            text: qsTr("Extract all textures…")
            enabled: menuRow.bridge.textureCount > 0
            onTriggered: menuRow.bridge.extractAll()
        }
        RetroMenuSeparator { }
        RetroMenuItem {
            text: qsTr("Close")
            shortcutText: "Ctrl+W"
            onTriggered: menuRow.viewerWindow.close()
        }
    }

    RetroMenu {
        id: helpMenu
        connectionWidth: helpButton.width
        RetroMenuItem {
            text: qsTr("About HikariHopper")
            onTriggered: about.show()
        }
    }

    AboutWindow { id: about }

    Shortcut { sequence: "Ctrl+S"; enabled: menuRow.bridge.canSaveSource; onActivated: menuRow.bridge.saveYtd() }
    Shortcut { sequence: "Ctrl+Shift+S"; enabled: menuRow.bridge.textureCount > 0; onActivated: menuRow.bridge.saveYtdAs() }
    Shortcut { sequence: "Ctrl+E"; enabled: menuRow.bridge.selectedIndex >= 0; onActivated: menuRow.bridge.extractSelected() }
    Shortcut { sequence: "Ctrl+W"; onActivated: menuRow.viewerWindow.close() }
}
