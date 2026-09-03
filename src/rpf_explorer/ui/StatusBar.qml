import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: statusBar
    required property var bridge

    // Each cell is separated by a hard seam plus a light edge — the bevelled
    // status bar a desktop tool has, not a flat strip.
    component Seam: Rectangle {
        Layout.preferredWidth: 2
        Layout.fillHeight: true
        color: Theme.Theme.borderHard
        Rectangle { anchors.right: parent.right; width: 1; height: parent.height; color: Theme.Theme.border }
    }

    color: Theme.Theme.chromeBg
    implicitHeight: Theme.Theme.statusHeight
    Layout.fillWidth: true

    Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.top: parent.top; height: 1; color: Theme.Theme.border }

    RowLayout {
        anchors.fill: parent
        anchors.topMargin: 1
        spacing: 0

        Text {
            Layout.preferredWidth: 190
            Layout.fillHeight: true
            leftPadding: 10
            text: statusBar.bridge.hasWorkspace
                ? qsTr("%1 items").arg(statusBar.bridge.visibleCount)
                : qsTr("NO GAME")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            verticalAlignment: Text.AlignVCenter
        }

        Seam { }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: 8
            Text {
                leftPadding: 10
                text: statusBar.bridge.hasSelection ? qsTr("Selected") : statusBar.bridge.status
                color: Theme.Theme.textFaint
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                verticalAlignment: Text.AlignVCenter
                elide: Text.ElideRight
            }
            Text {
                text: statusBar.bridge.selectedName
                color: Theme.Theme.text
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                verticalAlignment: Text.AlignVCenter
            }
            Text {
                text: statusBar.bridge.selectedSize
                color: Theme.Theme.textFaint
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                verticalAlignment: Text.AlignVCenter
                Layout.fillWidth: true
            }
        }

        Seam { }

        // Function keys, spelled the way a desktop tool spells them: the key in
        // bright ink, what it does in dim.
        RowLayout {
            Layout.fillHeight: true
            spacing: 14
            Repeater {
                model: [
                    { key: "Ctrl+O", label: qsTr("Game folder") },
                    { key: "Enter", label: qsTr("Open") },
                    { key: "Backspace", label: qsTr("Up") },
                    { key: "F3", label: qsTr("Search") }
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
                        verticalAlignment: Text.AlignVCenter
                    }
                    Text {
                        text: hint.modelData.label
                        color: Theme.Theme.textFaint
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.smallFontSize
                        verticalAlignment: Text.AlignVCenter
                    }
                }
            }
            Item { Layout.preferredWidth: 10 }
        }
    }
}
