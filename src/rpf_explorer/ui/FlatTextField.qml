import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

TextField {
    id: control

    // A key cap rendered inside the field, the way a desktop tool advertises
    // its shortcut — not a "⌘K"-style pill.
    property string keyCap: ""
    property string leadingIcon: ""
    property color leadingColor: Theme.Theme.textFaint

    hoverEnabled: true
    color: Theme.Theme.text
    placeholderTextColor: Theme.Theme.textFaint
    selectionColor: Theme.Theme.accentMuted
    selectedTextColor: Theme.Theme.text
    font.family: Theme.Theme.monoFont
    font.pixelSize: 11
    leftPadding: leadingIcon === "" ? 10 : 28
    rightPadding: keyCap === "" ? 10 : 40
    topPadding: 0
    bottomPadding: 0

    background: Rectangle {
        color: control.hovered && !control.activeFocus
            ? Qt.lighter(Theme.Theme.insetBg, 1.35)
            : Theme.Theme.insetBg
        border.width: 1
        border.color: control.activeFocus
            ? Theme.Theme.accent
            : control.hovered ? Theme.Theme.border : Theme.Theme.borderHard

        ChromeIcon {
            anchors.left: parent.left
            anchors.leftMargin: 8
            anchors.verticalCenter: parent.verticalCenter
            visible: control.leadingIcon !== ""
            kind: control.leadingIcon
            stroke: control.leadingColor
            implicitWidth: 13
            implicitHeight: 13
        }

        Rectangle {
            anchors.right: parent.right
            anchors.rightMargin: 4
            anchors.verticalCenter: parent.verticalCenter
            visible: control.keyCap !== ""
            width: capLabel.implicitWidth + 10
            height: 16
            color: Theme.Theme.chromeRaised
            border.width: 1
            border.color: Theme.Theme.border
            Text {
                id: capLabel
                anchors.centerIn: parent
                text: control.keyCap
                color: Theme.Theme.textDim
                font.family: Theme.Theme.monoFont
                font.pixelSize: 10
            }
        }
    }
}
