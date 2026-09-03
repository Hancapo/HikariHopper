import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: preview

    required property var bridge

    property bool fitMode: true
    property bool checkerboardVisible: true
    property bool filteredPreview: true
    property real manualScale: 1
    readonly property int selectedIndexMirror: bridge.selectedIndex
    readonly property real fittedScale: bridge.previewWidth > 0 && bridge.previewHeight > 0
        ? Math.min(
            Math.max(1, viewport.width - 48) / bridge.previewWidth,
            Math.max(1, viewport.height - 48) / bridge.previewHeight
        )
        : 1
    readonly property real displayScale: fitMode ? fittedScale : manualScale
    readonly property string zoomPercent: qsTr("%1%").arg(Math.round(displayScale * 100))

    color: Theme.Theme.appBg

    function fitTexture() {
        fitMode = true
        viewport.contentX = 0
        viewport.contentY = 0
    }

    function showActualSize() {
        fitMode = false
        manualScale = 1
        viewport.contentX = Math.max(0, (viewport.contentWidth - viewport.width) / 2)
        viewport.contentY = Math.max(0, (viewport.contentHeight - viewport.height) / 2)
    }

    function zoomBy(factor) {
        const current = displayScale
        fitMode = false
        manualScale = Math.max(0.05, Math.min(16, current * factor))
    }

    onSelectedIndexMirrorChanged: fitTexture()

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

        Flickable {
            id: viewport
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            boundsBehavior: Flickable.StopAtBounds
            contentWidth: Math.max(width, preview.bridge.previewWidth * preview.displayScale + 48)
            contentHeight: Math.max(height, preview.bridge.previewHeight * preview.displayScale + 48)

            Item {
                width: viewport.contentWidth
                height: viewport.contentHeight

                Item {
                    id: imageFrame
                    anchors.centerIn: parent
                    width: Math.max(1, preview.bridge.previewWidth * preview.displayScale)
                    height: Math.max(1, preview.bridge.previewHeight * preview.displayScale)
                    visible: preview.bridge.selectedIndex >= 0

                    TextureCheckerboard {
                        anchors.fill: parent
                        visible: preview.checkerboardVisible
                        cellSize: 24
                    }

                    Rectangle {
                        anchors.fill: parent
                        visible: !preview.checkerboardVisible
                        color: Theme.Theme.insetBg
                    }

                    Image {
                        anchors.fill: parent
                        source: preview.bridge.previewUrl
                        sourceSize.width: Math.max(1, preview.bridge.previewWidth)
                        sourceSize.height: Math.max(1, preview.bridge.previewHeight)
                        fillMode: Image.Stretch
                        asynchronous: true
                        cache: false
                        smooth: preview.filteredPreview
                        mipmap: preview.filteredPreview
                        visible: preview.bridge.previewUrl !== ""
                    }

                    Rectangle {
                        anchors.fill: parent
                        color: "transparent"
                        border.width: 1
                        border.color: Theme.Theme.border
                    }
                }
            }

            Text {
                anchors.centerIn: parent
                visible: preview.bridge.previewLoading || preview.bridge.selectedIndex < 0
                text: preview.bridge.previewLoading
                    ? qsTr("Decoding texture…")
                    : preview.bridge.status
                color: preview.bridge.error === ""
                    ? Theme.Theme.textFaint
                    : Theme.Theme.error
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
            }
        }

        TextureMipBar {
            Layout.fillWidth: true
            bridge: preview.bridge
        }

        TextureFactsBar {
            Layout.fillWidth: true
            bridge: preview.bridge
        }
    }

    Shortcut { sequence: "F"; onActivated: preview.fitTexture() }
    Shortcut { sequence: "1"; onActivated: preview.showActualSize() }
}
