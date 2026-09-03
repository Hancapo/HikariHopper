import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

Dialog {
    id: dialog

    required property var bridge

    signal saveRequested()
    signal discardRequested()

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
        Rectangle { anchors.left: parent.left; anchors.right: parent.right; height: 1; color: Theme.Theme.bevel }
        Rectangle { anchors.left: parent.left; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 1; color: Theme.Theme.borderHard }
        Text {
            x: 10
            width: parent.width - 20
            height: parent.height
            text: qsTr("UNSAVED CHANGES")
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
            name: "file"
            stroke: Theme.Theme.inkAsset
            Accessible.ignored: true
        }

        ColumnLayout {
            Layout.fillWidth: true
            Layout.rightMargin: 16
            spacing: 6

            Text {
                Layout.fillWidth: true
                text: qsTr("Save changes to %1?").arg(dialog.bridge.sourceName)
                color: Theme.Theme.text
                font.family: Theme.Theme.uiFont
                font.pixelSize: Theme.Theme.fontSize
                font.bold: true
            }
            Text {
                Layout.fillWidth: true
                text: qsTr("Closing now will discard the pending texture operations.")
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
        Rectangle { anchors.left: parent.left; anchors.right: parent.right; height: 1; color: Theme.Theme.border }

        RowLayout {
            anchors.right: parent.right
            anchors.rightMargin: 12
            anchors.verticalCenter: parent.verticalCenter
            spacing: 6

            ChromeToolButton {
                Layout.preferredWidth: 82
                Layout.preferredHeight: 28
                raised: true
                text: qsTr("Cancel")
                onClicked: dialog.reject()
            }
            ChromeToolButton {
                Layout.preferredWidth: 106
                Layout.preferredHeight: 28
                raised: true
                text: qsTr("Don't save")
                onClicked: {
                    dialog.close()
                    dialog.discardRequested()
                }
            }
            ChromeToolButton {
                Layout.preferredWidth: 82
                Layout.preferredHeight: 28
                primary: true
                enabled: !dialog.bridge.operationBusy
                text: qsTr("Save")
                onClicked: {
                    dialog.close()
                    dialog.saveRequested()
                }
            }
        }
    }
}
