import QtQuick
import "theme" as Theme

Item {
    id: checkerboard

    property int cellSize: 24

    onCellSizeChanged: board.requestPaint()

    Canvas {
        id: board

        anchors.fill: parent
        onWidthChanged: requestPaint()
        onHeightChanged: requestPaint()
        onPaint: {
            const context = getContext("2d")
            context.fillStyle = Theme.Theme.chromeBg
            context.fillRect(0, 0, width, height)
            context.fillStyle = Theme.Theme.appBg
            for (let y = 0; y < height; y += checkerboard.cellSize) {
                const row = Math.floor(y / checkerboard.cellSize)
                for (let x = 0; x < width; x += checkerboard.cellSize) {
                    const column = Math.floor(x / checkerboard.cellSize)
                    if ((row + column) % 2 !== 0)
                        context.fillRect(
                            x,
                            y,
                            checkerboard.cellSize,
                            checkerboard.cellSize
                        )
                }
            }
        }
    }
}
