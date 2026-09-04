import QtQuick
import "theme" as Theme

Rectangle {
    id: loadingView

    required property var bridge
    property bool revealed: false

    visible: revealed && bridge.gameLoading
    z: 10
    color: Theme.Theme.appBg

    Accessible.role: Accessible.ProgressBar
    Accessible.name: qsTr("Loading %1").arg(bridge.gameLoadingName)
    Accessible.ignored: !visible

    Timer {
        interval: 200
        running: loadingView.bridge.gameLoading && !loadingView.revealed
        onTriggered: loadingView.revealed = true
    }

    Connections {
        target: loadingView.bridge

        function onGameLoadingChanged() {
            if (!loadingView.bridge.gameLoading)
                loadingView.revealed = false
        }
    }

    Column {
        anchors.centerIn: parent
        anchors.verticalCenterOffset: -28
        spacing: 9

        Text {
            anchors.horizontalCenter: parent.horizontalCenter
            text: qsTr("LOADING GAME")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1.2
        }

        Text {
            anchors.horizontalCenter: parent.horizontalCenter
            text: loadingView.bridge.gameLoadingName
            color: Theme.Theme.text
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
        }

        Rectangle {
            width: 220
            height: 2
            color: Theme.Theme.borderHard

            Rectangle {
                id: progressMark

                width: 56
                height: parent.height
                color: Theme.Theme.accent
            }

            SequentialAnimation {
                running: loadingView.visible
                loops: Animation.Infinite

                XAnimator {
                    target: progressMark
                    from: 0
                    to: 164
                    duration: 420
                    easing.type: Easing.InOutCubic
                }
                XAnimator {
                    target: progressMark
                    from: 164
                    to: 0
                    duration: 420
                    easing.type: Easing.InOutCubic
                }
            }
        }
    }
}
