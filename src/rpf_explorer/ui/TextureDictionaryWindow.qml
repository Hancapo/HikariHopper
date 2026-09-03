import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Window {
    id: window

    required property var bridge

    visible: false
    width: 1250
    height: 738
    minimumWidth: 1080
    minimumHeight: 620
    title: bridge.sourceName === ""
        ? qsTr("Texture Dictionary — HikariHopper")
        : qsTr("%1%2 — Texture Dictionary — HikariHopper")
            .arg(bridge.sourceName)
            .arg(bridge.modified && !bridge.saving ? " *" : "")
    color: Theme.Theme.appBg
    flags: Qt.Window | Qt.FramelessWindowHint
    modality: Qt.NonModal

    property bool closeApproved: false
    property bool closeAfterSave: false

    function present() {
        window.show()
        window.raise()
        window.requestActivate()
    }

    function openToolDialog(component) {
        toolDialogLoader.active = false
        toolDialogLoader.sourceComponent = component
        toolDialogLoader.active = true
    }

    function openUnsavedDialog() {
        if (unsavedDialogLoader.status === Loader.Ready) {
            unsavedDialogLoader.item.visible = true
            return
        }
        unsavedDialogLoader.active = true
    }

    function saveAndClose() {
        closeAfterSave = bridge.canSaveSource
            ? bridge.saveYtd()
            : bridge.saveYtdAs()
    }

    Connections {
        target: window.bridge
        function onOpenRequested() {
            window.closeApproved = false
            window.closeAfterSave = false
            window.present()
        }
        function onSaveFinished(success) {
            if (!window.closeAfterSave)
                return
            window.closeAfterSave = false
            if (success) {
                window.closeApproved = true
                window.close()
            }
        }
    }

    onClosing: close => {
        if (window.bridge.saving) {
            close.accepted = false
            window.closeAfterSave = true
            return
        }
        if (!window.closeApproved && window.bridge.modified) {
            close.accepted = false
            window.openUnsavedDialog()
        }
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        WindowTitleBar {
            Layout.fillWidth: true
            targetWindow: window
            titleText: window.title
        }

        TextureViewerMenuRow {
            Layout.fillWidth: true
            bridge: window.bridge
            viewerWindow: window
        }

        SplitView {
            Layout.fillWidth: true
            Layout.fillHeight: true
            orientation: Qt.Horizontal

            handle: SplitViewHandle { }

            TextureRail {
                SplitView.preferredWidth: Theme.Theme.textureRailWidth
                SplitView.minimumWidth: 280
                SplitView.maximumWidth: 440
                bridge: window.bridge
                onResizeRequested: window.openToolDialog(resizeDialogComponent)
                onMipmapsRequested: window.openToolDialog(mipmapsDialogComponent)
                onFormatRequested: window.openToolDialog(formatDialogComponent)
                onAlphaRepairRequested: window.openToolDialog(alphaDialogComponent)
                onRenameRequested: window.openToolDialog(renameDialogComponent)
                onRemoveRequested: window.openToolDialog(removeDialogComponent)
            }

            TexturePreview {
                SplitView.fillWidth: true
                bridge: window.bridge
            }
        }

        TextureViewerStatusBar {
            Layout.fillWidth: true
            bridge: window.bridge
        }
    }

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

    Loader {
        id: toolDialogLoader
        active: false
        asynchronous: false
        onLoaded: item.visible = true
    }

    Loader {
        id: unsavedDialogLoader
        active: false
        asynchronous: false
        sourceComponent: unsavedDialogComponent
        onLoaded: item.visible = true
    }

    Component {
        id: unsavedDialogComponent
        TextureUnsavedChangesDialog {
            bridge: window.bridge
            onSaveRequested: window.saveAndClose()
            onDiscardRequested: {
                window.closeApproved = true
                window.close()
            }
            onClosed: Qt.callLater(() => unsavedDialogLoader.active = false)
        }
    }

    Component {
        id: resizeDialogComponent
        TextureResizeDialog {
            bridge: window.bridge
            onClosed: Qt.callLater(() => toolDialogLoader.active = false)
        }
    }

    Component {
        id: mipmapsDialogComponent
        TextureMipmapsDialog {
            bridge: window.bridge
            onClosed: Qt.callLater(() => toolDialogLoader.active = false)
        }
    }

    Component {
        id: formatDialogComponent
        TextureFormatDialog {
            bridge: window.bridge
            onClosed: Qt.callLater(() => toolDialogLoader.active = false)
        }
    }

    Component {
        id: alphaDialogComponent
        TextureAlphaRepairDialog {
            bridge: window.bridge
            onClosed: Qt.callLater(() => toolDialogLoader.active = false)
        }
    }

    Component {
        id: renameDialogComponent
        TextureRenameDialog {
            bridge: window.bridge
            onClosed: Qt.callLater(() => toolDialogLoader.active = false)
        }
    }

    Component {
        id: removeDialogComponent
        TextureRemoveDialog {
            bridge: window.bridge
            onClosed: Qt.callLater(() => toolDialogLoader.active = false)
        }
    }

    Shortcut {
        sequence: "F2"
        enabled: window.bridge.selectedIndex >= 0 && !window.bridge.operationBusy
        onActivated: window.openToolDialog(renameDialogComponent)
    }
    Shortcut {
        sequence: "Del"
        enabled: window.bridge.textureCount > 1 && !window.bridge.operationBusy
        onActivated: window.openToolDialog(removeDialogComponent)
    }
    Shortcut {
        sequence: "Ctrl+Z"
        enabled: window.bridge.canUndo
        onActivated: window.bridge.undo()
    }
}
