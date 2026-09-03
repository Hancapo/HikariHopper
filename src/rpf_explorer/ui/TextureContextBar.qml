import QtQuick
import QtQuick.Layouts
import "theme" as Theme

Rectangle {
    id: contextBar

    required property var bridge

    implicitHeight: Theme.Theme.textureContextHeight
    color: Theme.Theme.navigationBg

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        height: 1
        color: Theme.Theme.borderHard
    }

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 12
        anchors.rightMargin: 14
        spacing: 10

        LucideIcon {
            Layout.preferredWidth: 18
            Layout.preferredHeight: 18
            name: "book-image"
            stroke: Theme.Theme.inkAsset
            Accessible.ignored: true
        }

        ColumnLayout {
            Layout.fillWidth: true
            spacing: 0

            Text {
                Layout.fillWidth: true
                text: contextBar.bridge.sourceName
                color: Theme.Theme.text
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.fontSize
                font.bold: true
                elide: Text.ElideRight
            }

            Text {
                Layout.fillWidth: true
                text: contextBar.bridge.sourcePath
                color: Theme.Theme.textFaint
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                elide: Text.ElideMiddle
            }
        }

        Text {
            text: contextBar.bridge.loading
                ? qsTr("READING")
                : qsTr("%1 textures").arg(contextBar.bridge.textureCount)
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
        }
    }
}
