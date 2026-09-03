import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: statusBar

    required property var bridge

    component Seam: Rectangle {
        Layout.preferredWidth: 2
        Layout.fillHeight: true
        color: Theme.Theme.borderHard
        Rectangle {
            anchors.right: parent.right
            width: 1
            height: parent.height
            color: Theme.Theme.border
        }
    }

    implicitHeight: Theme.Theme.statusHeight
    color: Theme.Theme.chromeBg

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
        height: 1
        color: Theme.Theme.border
    }

    RowLayout {
        anchors.fill: parent
        anchors.topMargin: 1
        spacing: 0

        Text {
            Layout.preferredWidth: 150
            Layout.fillHeight: true
            leftPadding: 10
            text: qsTr("%1 textures").arg(statusBar.bridge.textureCount)
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            verticalAlignment: Text.AlignVCenter
        }

        Seam { }

        Text {
            Layout.fillWidth: true
            Layout.fillHeight: true
            leftPadding: 10
            text: statusBar.bridge.operationBusy || statusBar.bridge.error !== ""
                ? statusBar.bridge.status
                : ""
            color: statusBar.bridge.error === ""
                ? Theme.Theme.textFaint
                : Theme.Theme.error
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            verticalAlignment: Text.AlignVCenter
            elide: Text.ElideRight
        }

        Seam { }

        RowLayout {
            Layout.fillHeight: true
            Layout.leftMargin: Theme.Theme.statusHintInset
            spacing: 14
            Repeater {
                model: [
                    { key: "Ctrl+E", label: qsTr("Extract DDS") },
                    { key: "F", label: qsTr("Fit") },
                    { key: "1", label: qsTr("Actual size") }
                ]
                delegate: RowLayout {
                    id: hint
                    required property var modelData
                    spacing: 5
                    Text {
                        text: hint.modelData.key
                        color: Theme.Theme.textRow
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.smallFontSize
                    }
                    Text {
                        text: hint.modelData.label
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.smallFontSize
                    }
                }
            }
            Item { Layout.preferredWidth: Theme.Theme.statusHintInset }
        }
    }
}
