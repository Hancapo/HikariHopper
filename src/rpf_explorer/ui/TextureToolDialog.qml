import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

Dialog {
    id: dialog

    required property var bridge
    default property alias dialogContent: body.data

    property string heading: ""
    property string applyLabel: qsTr("Apply")
    property bool applyEnabled: true
    property int bodyHeight: 280
    property var applyAction: null

    modal: true
    parent: Overlay.overlay
    x: Math.round((parent.width - width) / 2)
    y: Math.round((parent.height - height) / 2)
    width: 520
    height: Theme.Theme.headerHeight + bodyHeight + 58
    padding: 0
    closePolicy: Popup.CloseOnEscape

    Overlay.modal: Rectangle {
        color: Theme.Theme.marqueeFill
    }

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
            text: dialog.heading
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
            verticalAlignment: Text.AlignVCenter
            elide: Text.ElideRight
        }
    }

    contentItem: Item {
        id: body
        implicitHeight: dialog.bodyHeight
        Accessible.name: dialog.heading
        Accessible.role: Accessible.Dialog
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

        ChromeToolButton {
            anchors.right: applyButton.left
            anchors.rightMargin: 6
            anchors.verticalCenter: parent.verticalCenter
            width: 88
            height: 28
            raised: true
            text: qsTr("Cancel")
            onClicked: dialog.reject()
        }

        ChromeToolButton {
            id: applyButton
            anchors.right: parent.right
            anchors.rightMargin: 12
            anchors.verticalCenter: parent.verticalCenter
            width: 88
            height: 28
            primary: true
            enabled: dialog.applyEnabled && !dialog.bridge.operationBusy
            text: dialog.applyLabel
            onClicked: {
                if (typeof dialog.applyAction === "function" && dialog.applyAction())
                    dialog.accept()
            }
        }
    }
}
