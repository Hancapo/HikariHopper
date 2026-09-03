import QtQuick
import "theme" as Theme

Item {
    id: glyph

    required property string kind
    property color ink: Theme.Theme.textDim
    property bool pressed: false

    implicitWidth: 18
    implicitHeight: 18
    Accessible.ignored: true

    Item {
        width: 18
        height: 18
        anchors.centerIn: parent
        transform: Translate { y: glyph.pressed ? 1 : 0 }

        Rectangle {
            visible: glyph.kind === "minimize"
            x: 4
            y: 11
            width: 10
            height: 1
            color: glyph.ink
        }

        Rectangle {
            visible: glyph.kind === "maximize"
            x: 4
            y: 4
            width: 10
            height: 10
            color: "transparent"
            border.width: 1
            border.color: glyph.ink
        }

        Item {
            anchors.fill: parent
            visible: glyph.kind === "restore"

            Rectangle { x: 6; y: 4; width: 9; height: 1; color: glyph.ink }
            Rectangle { x: 14; y: 4; width: 1; height: 9; color: glyph.ink }
            Rectangle { x: 6; y: 4; width: 1; height: 3; color: glyph.ink }
            Rectangle { x: 12; y: 12; width: 3; height: 1; color: glyph.ink }
            Rectangle {
                x: 3
                y: 6
                width: 10
                height: 9
                color: Theme.Theme.chromeRaised
                border.width: 1
                border.color: glyph.ink
            }
        }

        Item {
            anchors.fill: parent
            visible: glyph.kind === "close"

            Rectangle {
                anchors.centerIn: parent
                width: 11
                height: 1
                rotation: 45
                color: glyph.ink
            }
            Rectangle {
                anchors.centerIn: parent
                width: 11
                height: 1
                rotation: -45
                color: glyph.ink
            }
        }
    }
}
