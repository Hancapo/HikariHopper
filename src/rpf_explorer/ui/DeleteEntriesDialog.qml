import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

Dialog {
    id: dialog

    required property var bridge

    readonly property int fileCount: bridge.selectionCount
    readonly property string question: fileCount === 1
        ? qsTr("Are you sure you want to delete “%1”?").arg(bridge.selectedName)
        : qsTr("Are you sure you want to delete %1 items?").arg(fileCount)

    function submit() {
        if (dialog.bridge.deleteSelectedFiles())
            accept()
    }

    modal: true
    parent: Overlay.overlay
    x: Math.round((parent.width - width) / 2)
    y: Math.round((parent.height - height) / 2)
    width: 410
    height: Theme.Theme.headerHeight + 64 + 58
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
            text: qsTr("DELETION")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
            verticalAlignment: Text.AlignVCenter
        }
    }

    contentItem: Text {
        leftPadding: 16
        rightPadding: 16
        text: dialog.question
        color: Theme.Theme.text
        font.family: Theme.Theme.uiFont
        font.pixelSize: Theme.Theme.fontSize
        font.bold: true
        verticalAlignment: Text.AlignVCenter
        wrapMode: Text.WordWrap
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
                objectName: "confirmDeleteButton"
                Layout.preferredWidth: 88
                Layout.preferredHeight: 28
                destructive: true
                enabled: !dialog.bridge.entryOperationBusy
                text: qsTr("Delete")
                onClicked: dialog.submit()
            }
        }
    }
}
