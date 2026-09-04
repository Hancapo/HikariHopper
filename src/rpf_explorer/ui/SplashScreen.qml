import QtQuick
import QtQuick.Shapes
import "theme" as Theme

Window {
    id: splash

    property string startupMessage: qsTr("Starting Qt interface…")
    property string startupPhase: qsTr("BOOT")

    width: Theme.Theme.splashWidth
    height: Theme.Theme.splashHeight
    x: Math.round((Screen.width - width) / 2)
    y: Math.round((Screen.height - height) / 2)
    visible: true
    color: Theme.Theme.appBg
    flags: Qt.SplashScreen | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint
    title: qsTr("HikariHopper")

    // DESIGN.md section 17 registers this own-window identity surface as the
    // one deliberate exception where green is expressive rather than state.
    Shape {
        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer
        Accessible.ignored: true

        ShapePath {
            fillColor: Theme.Theme.selection
            strokeWidth: -1
            startX: 350; startY: 0
            PathLine { x: 530; y: 0 }
            PathLine { x: 720; y: 376 }
            PathLine { x: 540; y: 376 }
        }

        ShapePath {
            fillColor: Theme.Theme.windowChrome
            strokeWidth: -1
            startX: 394; startY: 0
            PathLine { x: 486; y: 0 }
            PathLine { x: 676; y: 376 }
            PathLine { x: 584; y: 376 }
        }

        ShapePath {
            fillColor: "transparent"
            strokeColor: Theme.Theme.borderAccent
            strokeWidth: 1
            startX: 350; startY: 0
            PathLine { x: 540; y: 376 }
        }
    }

    Item {
        x: 46
        y: 62
        width: 330
        height: 92
        Accessible.ignored: true

        Rectangle {
            id: emblem
            width: 18
            height: 18
            color: Theme.Theme.brass

            Rectangle {
                anchors.centerIn: parent
                width: 8
                height: 8
                color: Theme.Theme.windowChrome
            }
        }

        Text {
            anchors.left: emblem.right
            anchors.leftMargin: 12
            anchors.top: emblem.top
            anchors.topMargin: -10
            text: qsTr("HikariHopper")
            color: Theme.Theme.text
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.splashTitleSize
            font.bold: true
        }

        Text {
            anchors.left: emblem.left
            anchors.top: emblem.bottom
            anchors.topMargin: 14
            text: qsTr("RPF EXPLORER")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
        }
    }

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        height: Theme.Theme.statusHeight
        color: Theme.Theme.chromeBg

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            height: 1
            color: Theme.Theme.bevel
        }

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            color: Theme.Theme.borderHard
        }

        Text {
            anchors.left: parent.left
            anchors.leftMargin: 10
            anchors.right: runtimeLabel.left
            anchors.rightMargin: 18
            anchors.verticalCenter: parent.verticalCenter
            text: splash.startupMessage
            color: Theme.Theme.textRow
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            elide: Text.ElideRight
        }

        Text {
            id: runtimeLabel
            anchors.right: parent.right
            anchors.rightMargin: 10
            anchors.verticalCenter: parent.verticalCenter
            text: splash.startupPhase
            color: Theme.Theme.textFaint
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
        }
    }

    Rectangle {
        anchors.fill: parent
        color: "transparent"
        border.width: 1
        border.color: Theme.Theme.borderAccent
        Accessible.ignored: true
    }

}
