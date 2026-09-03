import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

ToolButton {
    id: control

    required property string kind
    property bool separated: true

    implicitWidth: 34
    implicitHeight: Theme.Theme.windowTitleHeight
    padding: 0
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus

    background: Item {
        Rectangle {
            anchors.left: parent.left
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            width: 1
            visible: control.separated
            color: Theme.Theme.borderHard
        }

        Rectangle {
            anchors.horizontalCenter: parent.horizontalCenter
            anchors.bottom: parent.bottom
            anchors.bottomMargin: 2
            width: 12
            height: 1
            visible: control.activeFocus
            color: Theme.Theme.textRow
        }
    }

    contentItem: WindowControlGlyph {
        kind: control.kind
        pressed: control.down
        ink: !control.enabled
            ? Theme.Theme.textFaint
            : control.hovered || control.activeFocus
                ? Theme.Theme.text
                : Theme.Theme.textDim
    }
}
