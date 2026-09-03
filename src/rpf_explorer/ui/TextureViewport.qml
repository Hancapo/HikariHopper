import QtQuick
import "theme" as Theme

Item {
    id: surface

    required property var bridge
    required property bool checkerboardVisible
    required property bool filteredPreview

    property bool fitMode: true
    property real manualScale: 1
    property real panX: 0
    property real panY: 0
    readonly property int keyboardPanStep: 48
    readonly property int selectedIndexMirror: bridge.selectedIndex
    readonly property real fittedScale: bridge.previewWidth > 0 && bridge.previewHeight > 0
        ? Math.min(
            Math.max(1, width - 48) / bridge.previewWidth,
            Math.max(1, height - 48) / bridge.previewHeight
        )
        : 1
    readonly property real displayScale: fitMode ? fittedScale : manualScale
    readonly property string zoomPercent: qsTr("%1%").arg(Math.round(displayScale * 100))

    objectName: "textureViewport"
    clip: true
    activeFocusOnTab: bridge.selectedIndex >= 0
    Accessible.name: qsTr("Texture preview")

    function fitTexture() {
        fitMode = true
        panX = 0
        panY = 0
    }

    function showActualSize() {
        fitMode = false
        manualScale = 1
        panX = 0
        panY = 0
    }

    function zoomBy(factor) {
        zoomAt(factor, width / 2, height / 2)
    }

    function zoomAt(factor, focusX, focusY) {
        if (bridge.selectedIndex < 0)
            return

        const currentScale = displayScale
        const currentImageX = (width - bridge.previewWidth * currentScale) / 2 + panX
        const currentImageY = (height - bridge.previewHeight * currentScale) / 2 + panY
        const clampedFocusX = Math.max(0, Math.min(width, focusX))
        const clampedFocusY = Math.max(0, Math.min(height, focusY))
        const imagePointX = (clampedFocusX - currentImageX) / currentScale
        const imagePointY = (clampedFocusY - currentImageY) / currentScale

        fitMode = false
        manualScale = Math.max(0.05, Math.min(16, currentScale * factor))
        panX = clampedFocusX
            - (width - bridge.previewWidth * manualScale) / 2
            - imagePointX * manualScale
        panY = clampedFocusY
            - (height - bridge.previewHeight * manualScale) / 2
            - imagePointY * manualScale
    }

    function panBy(xDistance, yDistance) {
        panX += xDistance
        panY += yDistance
    }

    onSelectedIndexMirrorChanged: fitTexture()
    Keys.onLeftPressed: panBy(-keyboardPanStep, 0)
    Keys.onRightPressed: panBy(keyboardPanStep, 0)
    Keys.onUpPressed: panBy(0, -keyboardPanStep)
    Keys.onDownPressed: panBy(0, keyboardPanStep)

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

    Item {
        id: imageFrame

        objectName: "textureImageFrame"
        x: (surface.width - width) / 2 + surface.panX
        y: (surface.height - height) / 2 + surface.panY
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

    DragHandler {
        id: panHandler

        target: null
        enabled: surface.bridge.selectedIndex >= 0
        cursorShape: active ? Qt.ClosedHandCursor : Qt.OpenHandCursor
        onActiveChanged: {
            if (active)
                surface.forceActiveFocus()
        }
        xAxis.onActiveValueChanged: delta => surface.panX += delta
        yAxis.onActiveValueChanged: delta => surface.panY += delta
    }

    WheelHandler {
        target: null
        acceptedDevices: PointerDevice.Mouse | PointerDevice.TouchPad
        onWheel: event => {
            const delta = event.angleDelta.y !== 0
                ? event.angleDelta.y
                : event.pixelDelta.y
            if (delta === 0) {
                event.accepted = false
                return
            }
            surface.zoomAt(Math.pow(1.25, delta / 120), event.x, event.y)
            event.accepted = true
        }
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
