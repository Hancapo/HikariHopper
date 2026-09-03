import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

AbstractButton {
    id: control

    required property bool sortActive
    required property bool sortAscending
    property bool alignRight: false

    hoverEnabled: true
    activeFocusOnTab: true
    Accessible.name: qsTr("Sort by %1").arg(control.text)
    Accessible.description: control.sortActive
        ? (control.sortAscending ? qsTr("Sorted ascending") : qsTr("Sorted descending"))
        : qsTr("Not sorted")

    background: Rectangle {
        color: control.hovered || control.visualFocus ? Theme.Theme.hoverBg : "transparent"
    }

    contentItem: Item {
        Row {
            x: control.alignRight
                ? Math.max(0, parent.width - width)
                : 0
            anchors.verticalCenter: parent.verticalCenter
            spacing: 6

            Text {
                anchors.verticalCenter: parent.verticalCenter
                text: control.text
                color: control.sortActive || control.hovered
                    ? Theme.Theme.textRow
                    : Theme.Theme.textDim
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                font.bold: true
                font.letterSpacing: 0.9
            }

            ChromeIcon {
                anchors.verticalCenter: parent.verticalCenter
                visible: control.sortActive
                kind: control.sortAscending ? "caretUp" : "caretDown"
                stroke: Theme.Theme.textRow
                thickness: 1.4
                implicitWidth: 10
                implicitHeight: 10
            }
        }
    }

    DottedRule {
        anchors.top: parent.top
        anchors.right: parent.right
        anchors.bottom: parent.bottom
    }
}
