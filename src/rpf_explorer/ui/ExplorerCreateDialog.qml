import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

Dialog {
    id: dialog

    required property var bridge
    property string creationKind: ""
    property string sourcePath: ""

    readonly property bool hasSource: sourcePath !== ""
    readonly property string heading: {
        switch (creationKind) {
        case "folder": return qsTr("NEW FOLDER")
        case "empty-rpf": return qsTr("NEW EMPTY RPF")
        case "folder-rpf": return qsTr("RPF FROM FOLDER")
        case "zip-rpf": return qsTr("RPF FROM ZIP")
        default: return qsTr("NEW ENTRY")
        }
    }

    function begin(kind, suggestedName, source) {
        creationKind = kind
        sourcePath = source
        nameField.text = suggestedName
        open()
    }

    function submit() {
        const name = nameField.text.trim()
        if (name === "" || bridge.creationBusy)
            return
        let started = false
        switch (creationKind) {
        case "folder":
            started = bridge.createFolder(name)
            break
        case "empty-rpf":
            started = bridge.createEmptyRpf(name)
            break
        case "folder-rpf":
            started = bridge.createRpfFromFolder(name, sourcePath)
            break
        case "zip-rpf":
            started = bridge.createRpfFromZip(name, sourcePath)
            break
        }
        if (started)
            accept()
    }

    modal: true
    parent: Overlay.overlay
    x: Math.round((parent.width - width) / 2)
    y: Math.round((parent.height - height) / 2)
    width: 480
    height: Theme.Theme.headerHeight + (hasSource ? 164 : 112) + 58
    padding: 0
    closePolicy: Popup.CloseOnEscape

    onOpened: {
        nameField.forceActiveFocus()
        nameField.selectAll()
    }

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
            text: dialog.heading
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
            verticalAlignment: Text.AlignVCenter
        }
    }

    contentItem: ColumnLayout {
        spacing: 6

        Text {
            Layout.fillWidth: true
            Layout.leftMargin: 16
            Layout.rightMargin: 16
            Layout.topMargin: 14
            text: qsTr("NAME")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
        }

        FlatTextField {
            id: nameField
            Layout.fillWidth: true
            Layout.leftMargin: 16
            Layout.rightMargin: 16
            Layout.preferredHeight: 32
            Accessible.name: qsTr("Entry name")
            Keys.onReturnPressed: dialog.submit()
            Keys.onEnterPressed: dialog.submit()
        }

        Text {
            Layout.fillWidth: true
            Layout.leftMargin: 16
            Layout.rightMargin: 16
            Layout.topMargin: 7
            visible: dialog.hasSource
            text: qsTr("SOURCE")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.leftMargin: 16
            Layout.rightMargin: 16
            Layout.preferredHeight: 30
            visible: dialog.hasSource
            color: Theme.Theme.insetBg
            border.width: 1
            border.color: Theme.Theme.borderHard

            Text {
                anchors.fill: parent
                leftPadding: 9
                rightPadding: 9
                text: dialog.sourcePath
                color: Theme.Theme.textDim
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                verticalAlignment: Text.AlignVCenter
                elide: Text.ElideMiddle
            }
        }

        Item { Layout.fillHeight: true }
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
                primary: true
                enabled: nameField.text.trim() !== "" && !dialog.bridge.creationBusy
                text: qsTr("Create")
                onClicked: dialog.submit()
            }
        }
    }
}
