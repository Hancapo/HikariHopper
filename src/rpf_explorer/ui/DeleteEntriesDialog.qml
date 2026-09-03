import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

Dialog {
    id: dialog

    required property var bridge

    readonly property int fileCount: bridge.selectionCount
    readonly property string selectionLabel: fileCount === 1
        ? qsTr("“%1”").arg(bridge.selectedName)
        : qsTr("%1 selected files").arg(fileCount)

    function submit() {
        close()
        Qt.callLater(dialog.bridge.deleteSelectedFiles)
    }

    modal: true
    parent: Overlay.overlay
    x: Math.round((parent.width - width) / 2)
    y: Math.round((parent.height - height) / 2)
    width: 470
    height: Theme.Theme.headerHeight + 126 + 58
    padding: 0
    closePolicy: Popup.CloseOnEscape

    Overlay.modal: Rectangle { color: Theme.Theme.marqueeFill }

    background: Rectangle {
        color: Theme.Theme.chromeBg
        border.width: 1
        border.color: Theme.Theme.borderHard
    }

    header: Rectangle {
        implicitHeight: Theme.Theme.headerHeight
        color: Theme.Theme.chromeRaised

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            height: 1
            color: Theme.Theme.bevel
        }
        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            color: Theme.Theme.borderHard
        }
        Text {
            x: 10
            width: parent.width - 20
            height: parent.height
            text: dialog.fileCount === 1
                ? qsTr("DELETE FILE")
                : qsTr("DELETE FILES")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
            verticalAlignment: Text.AlignVCenter
        }
    }

    contentItem: RowLayout {
        spacing: 12

        LucideIcon {
            Layout.leftMargin: 16
            Layout.preferredWidth: 24
            Layout.preferredHeight: 24
            name: "trash-2"
            stroke: Theme.Theme.error
            Accessible.ignored: true
        }

        ColumnLayout {
            Layout.fillWidth: true
            Layout.rightMargin: 16
            spacing: 6

            Text {
                Layout.fillWidth: true
                text: dialog.bridge.inArchive
                    ? qsTr("Delete %1 from this RPF?").arg(dialog.selectionLabel)
                    : qsTr("Move %1 to the Recycle Bin?").arg(dialog.selectionLabel)
                color: Theme.Theme.text
                font.family: Theme.Theme.uiFont
                font.pixelSize: Theme.Theme.fontSize
                font.bold: true
                wrapMode: Text.WordWrap
            }
            Text {
                Layout.fillWidth: true
                visible: dialog.bridge.inArchive
                text: qsTr("Files removed from an RPF cannot be restored from the Recycle Bin.")
                color: Theme.Theme.textDim
                font.family: Theme.Theme.uiFont
                font.pixelSize: Theme.Theme.fontSize
                wrapMode: Text.WordWrap
            }
        }
    }

    footer: Rectangle {
        implicitHeight: 58
        color: Theme.Theme.chromeRaised

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            height: 1
            color: Theme.Theme.border
        }

        RowLayout {
            anchors.right: parent.right
            anchors.rightMargin: 12
            anchors.verticalCenter: parent.verticalCenter
            spacing: 6

            ChromeToolButton {
                Layout.preferredWidth: 88
                Layout.preferredHeight: 28
                raised: true
                text: qsTr("Cancel")
                onClicked: dialog.reject()
            }
            ChromeToolButton {
                Layout.preferredWidth: 88
                Layout.preferredHeight: 28
                raised: true
                enabled: !dialog.bridge.entryOperationBusy
                foreground: Theme.Theme.error
                text: qsTr("Delete")
                onClicked: dialog.submit()
            }
        }
    }
}
