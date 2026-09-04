import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

MenuSeparator {
    id: control

    property string text: ""

    implicitWidth: Theme.Theme.menuMinimumWidth
    implicitHeight: 22
    leftPadding: 11
    rightPadding: 11
    topPadding: 0
    bottomPadding: 0

    contentItem: Text {
        text: control.text
        color: Theme.Theme.textDim
        font.family: Theme.Theme.monoFont
        font.pixelSize: Theme.Theme.smallFontSize
        font.bold: true
        font.letterSpacing: 1
        verticalAlignment: Text.AlignVCenter
    }
}
