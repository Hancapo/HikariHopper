import QtQuick
import "theme" as Theme

Item {
    id: surface

    required property var bridge
    required property bool checkerboardVisible
    required property bool filteredPreview

    property bool fitMode: true
    property real manualScale: 1
    property real pendingContentX: 0
    property real pendingContentY: 0
    readonly property int keyboardPanStep: 48
    readonly property int selectedIndexMirror: bridge.selectedIndex
    readonly property real fittedScale: bridge.previewWidth > 0 && bridge.previewHeight > 0
        ? Math.min(
            Math.max(1, viewport.width - 48) / bridge.previewWidth,
            Math.max(1, viewport.height - 48) / bridge.previewHeight
        )
        : 1
    readonly property real displayScale: fitMode ? fittedScale : manualScale
    readonly property string zoomPercent: qsTr("%1%").arg(Math.round(displayScale * 100))

    function fitTexture() {
        zoomPositionTimer.stop()
        fitMode = true
        viewport.contentX = 0
        viewport.contentY = 0
    }

    function showActualSize() {
        viewport.cancelFlick()
        fitMode = false
        manualScale = 1
        const contentSize = scaledContentSize(manualScale)
        pendingContentX = Math.max(0, (contentSize.width - viewport.width) / 2)
        pendingContentY = Math.max(0, (contentSize.height - viewport.height) / 2)
        zoomPositionTimer.restart()
    }

    function zoomBy(factor) {
        zoomAt(factor, viewport.width / 2, viewport.height / 2)
    }

    function scaledContentSize(scale) {
        return Qt.size(
            Math.max(viewport.width, bridge.previewWidth * scale + 48),
            Math.max(viewport.height, bridge.previewHeight * scale + 48)
        )
    }

    function zoomAt(factor, focusX, focusY) {
        if (bridge.selectedIndex < 0)
            return

        viewport.cancelFlick()
        const current = displayScale
        const oldContentSize = scaledContentSize(current)
        const oldImageWidth = bridge.previewWidth * current
        const oldImageHeight = bridge.previewHeight * current
        const oldImageX = (oldContentSize.width - oldImageWidth) / 2
        const oldImageY = (oldContentSize.height - oldImageHeight) / 2
        const clampedFocusX = Math.max(0, Math.min(viewport.width, focusX))
        const clampedFocusY = Math.max(0, Math.min(viewport.height, focusY))
        const currentContentX = zoomPositionTimer.running
            ? pendingContentX
            : viewport.contentX
        const currentContentY = zoomPositionTimer.running
            ? pendingContentY
            : viewport.contentY
        const imagePointX = (currentContentX + clampedFocusX - oldImageX) / current
        const imagePointY = (currentContentY + clampedFocusY - oldImageY) / current

        fitMode = false
        manualScale = Math.max(0.05, Math.min(16, current * factor))
        const newContentSize = scaledContentSize(manualScale)
        const newImageX = (newContentSize.width - bridge.previewWidth * manualScale) / 2
        const newImageY = (newContentSize.height - bridge.previewHeight * manualScale) / 2
        pendingContentX = Math.max(
            0,
            Math.min(
                newContentSize.width - viewport.width,
                newImageX + imagePointX * manualScale - clampedFocusX
            )
        )
        pendingContentY = Math.max(
            0,
            Math.min(
                newContentSize.height - viewport.height,
                newImageY + imagePointY * manualScale - clampedFocusY
            )
        )
        zoomPositionTimer.restart()
    }

    function panBy(xDistance, yDistance) {
        viewport.cancelFlick()
        viewport.contentX = Math.max(
            0,
            Math.min(viewport.contentWidth - viewport.width, viewport.contentX + xDistance)
        )
        viewport.contentY = Math.max(
            0,
            Math.min(viewport.contentHeight - viewport.height, viewport.contentY + yDistance)
        )
    }

    onSelectedIndexMirrorChanged: fitTexture()

    Timer {
        id: zoomPositionTimer

        interval: 0
        onTriggered: {
            viewport.contentX = surface.pendingContentX
            viewport.contentY = surface.pendingContentY
        }
    }

    TextureCheckerboard {
        objectName: "textureCheckerboard"
        anchors.fill: parent
        visible: surface.checkerboardVisible
        cellSize: Theme.Theme.textureCheckerCellSize
    }

    Rectangle {
        anchors.fill: parent
        visible: !surface.checkerboardVisible
        color: Theme.Theme.appBg
    }

    Flickable {
        id: viewport

        objectName: "textureViewport"
        anchors.fill: parent
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        flickableDirection: Flickable.AutoFlickDirection
        interactive: contentWidth > width || contentHeight > height
        activeFocusOnTab: interactive
        Accessible.name: qsTr("Texture preview")
        contentWidth: Math.max(
            width,
            surface.bridge.previewWidth * surface.displayScale + 48
        )
        contentHeight: Math.max(
            height,
            surface.bridge.previewHeight * surface.displayScale + 48
        )
        onDragStarted: forceActiveFocus()
        Keys.onLeftPressed: surface.panBy(-surface.keyboardPanStep, 0)
        Keys.onRightPressed: surface.panBy(surface.keyboardPanStep, 0)
        Keys.onUpPressed: surface.panBy(0, -surface.keyboardPanStep)
        Keys.onDownPressed: surface.panBy(0, surface.keyboardPanStep)

        Item {
            width: viewport.contentWidth
            height: viewport.contentHeight

            Item {
                id: imageFrame

                objectName: "textureImageFrame"
                anchors.centerIn: parent
                width: Math.max(1, surface.bridge.previewWidth * surface.displayScale)
                height: Math.max(1, surface.bridge.previewHeight * surface.displayScale)
                visible: surface.bridge.selectedIndex >= 0

                Image {
                    anchors.fill: parent
                    source: surface.bridge.previewUrl
                    sourceSize.width: Math.max(1, surface.bridge.previewWidth)
                    sourceSize.height: Math.max(1, surface.bridge.previewHeight)
                    fillMode: Image.Stretch
                    asynchronous: true
                    cache: false
                    smooth: surface.filteredPreview
                    mipmap: surface.filteredPreview
                    visible: surface.bridge.previewUrl !== ""
                }

                Rectangle {
                    anchors.fill: parent
                    color: "transparent"
                    border.width: 1
                    border.color: Theme.Theme.border
                }
            }
        }
    }

    WheelHandler {
        target: null
        acceptedDevices: PointerDevice.Mouse | PointerDevice.TouchPad
        onWheel: event => {
            const delta = event.angleDelta.y !== 0
                ? event.angleDelta.y
                : event.pixelDelta.y
            surface.zoomAt(Math.pow(1.25, delta / 120), event.x, event.y)
            event.accepted = true
        }
    }

    HoverHandler {
        cursorShape: viewport.interactive
            ? viewport.dragging ? Qt.ClosedHandCursor : Qt.OpenHandCursor
            : Qt.ArrowCursor
    }

    Text {
        anchors.centerIn: parent
        visible: surface.bridge.previewLoading || surface.bridge.selectedIndex < 0
        text: surface.bridge.previewLoading
            ? qsTr("Decoding texture…")
            : surface.bridge.status
        color: surface.bridge.error === ""
            ? Theme.Theme.textFaint
            : Theme.Theme.error
        font.family: Theme.Theme.monoFont
        font.pixelSize: Theme.Theme.smallFontSize
    }
}
