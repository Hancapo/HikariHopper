import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

CheckBox {
    id: control

    hoverEnabled: true
    spacing: 8
    font.family: Theme.Theme.uiFont
    font.pixelSize: Theme.Theme.fontSize

    indicator: Rectangle {
        implicitWidth: 16
        implicitHeight: 16
        x: control.leftPadding
        y: (control.height - height) / 2
        color: Theme.Theme.insetBg
        border.width: 1
        border.color: control.activeFocus || control.hovered
            ? Theme.Theme.accent
            : Theme.Theme.border

        Rectangle {
            anchors.fill: parent
            anchors.margins: 4
            visible: control.checked
            color: Theme.Theme.accent
        }
    }

    contentItem: Text {
        leftPadding: control.indicator.width + control.spacing
        text: control.text
        color: control.enabled ? Theme.Theme.textRow : Theme.Theme.textFaint
        font: control.font
        verticalAlignment: Text.AlignVCenter
    }
}
