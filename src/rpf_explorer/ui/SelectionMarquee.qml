import QtQuick
import "theme" as Theme

pragma ComponentBehavior: Bound

Item {
    id: marquee

    Accessible.ignored: true

    Rectangle {
        anchors.fill: parent
        color: Theme.Theme.marqueeFill
    }

    // Clipping is intentional here: the hatch must terminate exactly at the
    // rubber-band edge while its geometry changes under direct manipulation.
    Item {
        id: hatch
        anchors.fill: parent
        anchors.margins: 1
        clip: true

        readonly property real lineLength: Math.ceil(Math.sqrt(2) * (width + height)) + 2
        readonly property int lineCount: Math.ceil((width + height) / 8) + 3

        Repeater {
            model: hatch.lineCount

            delegate: Rectangle {
                required property int index
                x: index * 8 - hatch.height * 0.5 - 8
                y: (hatch.height - hatch.lineLength) * 0.5
                width: 1
                height: hatch.lineLength
                rotation: 45
                transformOrigin: Item.Center
                color: Theme.Theme.marqueeHatch
            }
        }
    }

    Rectangle {
        anchors.fill: parent
        color: "transparent"
        border.width: 1
        border.color: Theme.Theme.marqueeBorder
    }
}
