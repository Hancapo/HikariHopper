import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

MenuItem {
    id: control
    property string shortcutText: ""
    implicitWidth: Math.max(
        Theme.Theme.menuMinimumWidth,
        contentItem.implicitWidth + leftPadding + rightPadding
    )
    implicitHeight: Theme.Theme.menuItemHeight
    leftPadding: 24
    rightPadding: 11
    topPadding: 0
    bottomPadding: 0
    hoverEnabled: true

    indicator: Item {
        implicitWidth: 20
        implicitHeight: control.height
        visible: control.checkable

        ChromeIcon {
            anchors.centerIn: parent
            visible: control.checked
            width: 12
            height: 12
            kind: "check"
            stroke: control.highlighted ? Theme.Theme.text : Theme.Theme.textDim
            thickness: 1.8
        }
    }

    contentItem: RowLayout {
        spacing: 18

        Text {
            text: control.text
            color: !control.enabled
                ? Theme.Theme.textFaint
                : control.highlighted ? Theme.Theme.text : Theme.Theme.textRow
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
            verticalAlignment: Text.AlignVCenter
            Layout.fillWidth: true
        }

        Text {
            text: control.shortcutText
            visible: text !== ""
            color: control.enabled
                ? (control.highlighted ? Theme.Theme.text : Theme.Theme.textDim)
                : Theme.Theme.textFaint
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            horizontalAlignment: Text.AlignRight
            verticalAlignment: Text.AlignVCenter
        }
    }

    background: Rectangle {
        color: control.highlighted ? Theme.Theme.hoverChrome : "transparent"

        Rectangle {
            anchors.left: parent.left
            anchors.verticalCenter: parent.verticalCenter
            width: 2
            height: parent.height - 10
            visible: control.highlighted
            color: Theme.Theme.textRow
        }
    }
}
