import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: menuRow
    required property var tabs
    readonly property var bridge: tabs.activeBridge
    signal searchRequested()
    color: Theme.Theme.chromeBg
    implicitHeight: Theme.Theme.menuHeight
    Layout.fillWidth: true

    readonly property int popupOpticalOffset: 0

    function openPopup(menu, button) {
        const origin = button.mapToItem(menuRow, 0, 0)
        menu.x = origin.x + menuRow.popupOpticalOffset
        menu.y = menuRow.height - 1
        menu.open()
    }

    Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 1
        spacing: 0
        MenuBarButton { id: fileButton; text: qsTr("File"); menuOpen: fileMenu.visible; onClicked: menuRow.openPopup(fileMenu, fileButton) }
        MenuBarButton { id: editButton; text: qsTr("Edit"); menuOpen: editMenu.visible; onClicked: menuRow.openPopup(editMenu, editButton) }
        MenuBarButton { id: viewButton; text: qsTr("View"); menuOpen: viewMenu.visible; onClicked: menuRow.openPopup(viewMenu, viewButton) }
        MenuBarButton { id: goButton; text: qsTr("Go"); menuOpen: goMenu.visible; onClicked: menuRow.openPopup(goMenu, goButton) }
        MenuBarButton { id: helpButton; text: qsTr("Help"); menuOpen: helpMenu.visible; onClicked: menuRow.openPopup(helpMenu, helpButton) }
        Item { Layout.fillWidth: true }
    }

    RetroMenu {
        id: fileMenu
        connectionWidth: fileButton.width
        RetroMenuItem { text: qsTr("New tab"); shortcutText: "Ctrl+T"; onTriggered: menuRow.tabs.newTab() }
        RetroMenuSeparator { }
        RetroMenuItem {
            text: qsTr("Open configured game")
            shortcutText: "Ctrl+O"
            enabled: menuRow.tabs.gamePathSettings.enhancedPathValid
                || menuRow.tabs.gamePathSettings.legacyPathValid
            onTriggered: menuRow.bridge.openConfiguredGame("")
        }
        RetroMenuItem { text: qsTr("Open RPF archive…"); shortcutText: "Ctrl+Shift+O"; onTriggered: menuRow.bridge.openArchiveDialog() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Close RPF archive"); enabled: menuRow.bridge.hasArchive; onTriggered: menuRow.bridge.closeArchive() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Close tab"); shortcutText: "Ctrl+W"; onTriggered: menuRow.tabs.closeActiveTab() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Exit"); shortcutText: "Alt+F4"; onTriggered: Qt.quit() }
    }

    RetroMenu {
        id: editMenu
        connectionWidth: editButton.width
        RetroMenuItem { text: qsTr("Copy name"); shortcutText: "Ctrl+C"; enabled: menuRow.bridge.hasSelection; onTriggered: menuRow.bridge.copySelectedName() }
        RetroMenuItem { text: qsTr("Copy path"); shortcutText: "Ctrl+Shift+C"; enabled: menuRow.bridge.hasSelection; onTriggered: menuRow.bridge.copySelectedPath() }
        RetroMenuItem {
            text: qsTr("Delete")
            shortcutText: "Del"
            iconName: "trash-2"
            enabled: menuRow.bridge.selectionDeletable
                && !menuRow.bridge.entryOperationBusy
            onTriggered: Qt.callLater(menuRow.bridge.requestDeleteSelection)
        }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Select all"); shortcutText: "Ctrl+A"; enabled: menuRow.bridge.visibleCount > 0; onTriggered: menuRow.bridge.selectAllEntries() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Search current location…"); shortcutText: "F3"; enabled: menuRow.bridge.hasWorkspace; onTriggered: menuRow.searchRequested() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Settings…"); onTriggered: settingsWindow.show() }
    }

    SettingsWindow {
        id: settingsWindow
        gamePaths: menuRow.tabs.gamePathSettings
    }

    RetroMenu {
        id: viewMenu
        connectionWidth: viewButton.width
        RetroMenuItem { text: qsTr("List view"); checkable: true; autoExclusive: true; checked: menuRow.bridge.viewMode === "list"; onTriggered: menuRow.bridge.setViewMode("list") }
        RetroMenuItem { text: qsTr("Grid view"); checkable: true; autoExclusive: true; checked: menuRow.bridge.viewMode === "grid"; onTriggered: menuRow.bridge.setViewMode("grid") }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Folders pane"); checkable: true; checked: menuRow.bridge.foldersVisible; onToggled: menuRow.bridge.setFoldersVisible(checked) }
    }

    RetroMenu {
        id: goMenu
        connectionWidth: goButton.width
        RetroMenuItem { text: qsTr("Back"); shortcutText: "Alt+Left"; enabled: menuRow.bridge.canGoBack; onTriggered: menuRow.bridge.goBack() }
        RetroMenuItem { text: qsTr("Forward"); shortcutText: "Alt+Right"; enabled: menuRow.bridge.canGoForward; onTriggered: menuRow.bridge.goForward() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("Up one level"); shortcutText: "Backspace"; enabled: menuRow.bridge.canGoUp; onTriggered: menuRow.bridge.goUp() }
    }

    RetroMenu {
        id: helpMenu
        connectionWidth: helpButton.width
        RetroMenuItem { text: qsTr("Keyboard shortcuts"); onTriggered: shortcuts.show() }
        RetroMenuSeparator { }
        RetroMenuItem { text: qsTr("About HikariHopper"); onTriggered: about.show() }
    }

    Shortcut { sequence: "Ctrl+T"; onActivated: menuRow.tabs.newTab() }
    Shortcut {
        sequence: "Ctrl+O"
        enabled: menuRow.tabs.gamePathSettings.enhancedPathValid
            || menuRow.tabs.gamePathSettings.legacyPathValid
        onActivated: menuRow.bridge.openConfiguredGame("")
    }
    Shortcut { sequence: "Ctrl+Shift+O"; onActivated: menuRow.bridge.openArchiveDialog() }
    Shortcut { sequence: "Ctrl+W"; onActivated: menuRow.tabs.closeActiveTab() }
    Shortcut { sequence: "Ctrl+C"; enabled: menuRow.bridge.hasSelection; onActivated: menuRow.bridge.copySelectedName() }
    Shortcut { sequence: "Ctrl+Shift+C"; enabled: menuRow.bridge.hasSelection; onActivated: menuRow.bridge.copySelectedPath() }

    ShortcutsWindow { id: shortcuts }
    AboutWindow { id: about }
}
