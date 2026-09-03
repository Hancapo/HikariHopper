import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: preview

    required property var bridge

    property bool checkerboardVisible: true
    property bool filteredPreview: true
    readonly property bool fitMode: textureViewport.fitMode
    readonly property real displayScale: textureViewport.displayScale
    readonly property string zoomPercent: textureViewport.zoomPercent

    color: Theme.Theme.appBg

    function fitTexture() {
        textureViewport.fitTexture()
    }

    function showActualSize() {
        textureViewport.showActualSize()
    }

    function zoomBy(factor) {
        textureViewport.zoomBy(factor)
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        TexturePreviewToolbar {
            Layout.fillWidth: true
            bridge: preview.bridge
            fitMode: preview.fitMode
            checkerboardVisible: preview.checkerboardVisible
            filteredPreview: preview.filteredPreview
            zoomPercent: preview.zoomPercent
            onFitRequested: preview.fitTexture()
            onActualSizeRequested: preview.showActualSize()
            onZoomInRequested: preview.zoomBy(1.25)
            onZoomOutRequested: preview.zoomBy(0.8)
            onCheckerboardToggled: preview.checkerboardVisible = !preview.checkerboardVisible
            onFilteredPreviewToggled: preview.filteredPreview = !preview.filteredPreview
        }

        TextureViewport {
            id: textureViewport

            Layout.fillWidth: true
            Layout.fillHeight: true
            bridge: preview.bridge
            checkerboardVisible: preview.checkerboardVisible
            filteredPreview: preview.filteredPreview
        }

        TextureMipBar {
            Layout.fillWidth: true
            bridge: preview.bridge
        }
    }

    Shortcut { sequence: "F"; onActivated: preview.fitTexture() }
    Shortcut { sequence: "1"; onActivated: preview.showActualSize() }
}
