.pragma library

function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value))
}

function navigationTarget(current, count, columns, direction) {
    if (count <= 0)
        return -1
    const start = current >= 0 ? current : 0
    let delta = 0
    if (direction === "left")
        delta = -1
    else if (direction === "right")
        delta = 1
    else if (direction === "up")
        delta = -columns
    else if (direction === "down")
        delta = columns
    return clamp(start + delta, 0, count - 1)
}

function indicesInRect(count, columns, cellWidth, cellHeight, leftMargin, topMargin,
                       left, right, top, bottom) {
    if (count <= 0 || columns <= 0 || right <= leftMargin || bottom <= topMargin)
        return []

    const rowCount = Math.ceil(count / columns)
    const contentRight = leftMargin + columns * cellWidth
    const contentBottom = topMargin + rowCount * cellHeight
    if (left >= contentRight || top >= contentBottom)
        return []

    const epsilon = 0.001
    const firstColumn = clamp(
        Math.floor((Math.max(left, leftMargin) - leftMargin) / cellWidth),
        0,
        columns - 1
    )
    const lastColumn = clamp(
        Math.floor((Math.min(right, contentRight) - leftMargin - epsilon) / cellWidth),
        0,
        columns - 1
    )
    const firstRow = clamp(
        Math.floor((Math.max(top, topMargin) - topMargin) / cellHeight),
        0,
        rowCount - 1
    )
    const lastRow = clamp(
        Math.floor((Math.min(bottom, contentBottom) - topMargin - epsilon) / cellHeight),
        0,
        rowCount - 1
    )

    const indices = []
    for (let row = firstRow; row <= lastRow; ++row) {
        for (let column = firstColumn; column <= lastColumn; ++column) {
            const index = row * columns + column
            if (index < count)
                indices.push(index)
        }
    }
    return indices
}
