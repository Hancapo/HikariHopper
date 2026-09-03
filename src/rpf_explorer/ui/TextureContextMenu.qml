import QtQuick

RetroMenu {
    id: menu

    required property var bridge

    signal resizeRequested()
    signal mipmapsRequested()
    signal formatRequested()
    signal alphaRepairRequested()
    signal renameRequested()
    signal removeRequested()

    RetroMenuItem {
        text: qsTr("Replace from image…")
        enabled: menu.bridge.selectedIndex >= 0 && !menu.bridge.operationBusy
        onTriggered: menu.bridge.replaceSelectedFromImage()
    }
    RetroMenuSeparator { }
    RetroMenuItem {
        text: qsTr("Resize texture…")
        enabled: menu.bridge.selectedIndex >= 0 && !menu.bridge.operationBusy
        onTriggered: menu.resizeRequested()
    }
    RetroMenuItem {
        text: qsTr("Recalculate mipmaps…")
        enabled: menu.bridge.selectedIndex >= 0 && !menu.bridge.operationBusy
        onTriggered: menu.mipmapsRequested()
    }
    RetroMenuItem {
        text: qsTr("Change format…")
        enabled: menu.bridge.selectedIndex >= 0 && !menu.bridge.operationBusy
        onTriggered: menu.formatRequested()
    }
    RetroMenuItem {
        text: qsTr("Repair alpha edges…")
        enabled: menu.bridge.selectedIndex >= 0 && !menu.bridge.operationBusy
        onTriggered: menu.alphaRepairRequested()
    }
    RetroMenuSeparator { }
    RetroMenuItem {
        text: qsTr("Extract DDS…")
        shortcutText: "Ctrl+E"
        enabled: menu.bridge.selectedIndex >= 0
        onTriggered: menu.bridge.extractSelected()
    }
    RetroMenuItem {
        text: qsTr("Rename…")
        shortcutText: "F2"
        enabled: menu.bridge.selectedIndex >= 0 && !menu.bridge.operationBusy
        onTriggered: menu.renameRequested()
    }
    RetroMenuItem {
        text: qsTr("Remove")
        shortcutText: "Del"
        enabled: menu.bridge.textureCount > 1 && !menu.bridge.operationBusy
        onTriggered: menu.removeRequested()
    }
}
