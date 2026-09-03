import QtQuick
import "theme" as Theme

Rectangle {
    id: row

    required property string gameName
    property string gamePath: ""
    property string expectedExecutable: ""
    property bool supported: false
    property bool pathValid: false

    signal pathCommitted(string path)
    signal browseRequested()

    implicitHeight: supported ? 62 : 45
    color: "transparent"

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        height: 1
        color: Theme.Theme.borderSoft
    }

    Text {
        x: 12
        y: row.supported ? 10 : 13
        width: 214
        text: row.gameName
        color: row.supported ? Theme.Theme.textRow : Theme.Theme.textFaint
        font.family: Theme.Theme.uiFont
        font.pixelSize: Theme.Theme.fontSize
        elide: Text.ElideRight
    }

    FlatTextField {
        id: pathField

        x: 238
        y: row.supported ? 7 : 8
        width: parent.width - x - 96
        height: 28
        enabled: row.supported
        opacity: row.supported ? 1 : 0.48
        text: row.gamePath
        placeholderText: row.supported
            ? qsTr("Select the installation folder")
            : qsTr("Future support")
        selectByMouse: true
        onEditingFinished: row.pathCommitted(text)
        Component.onCompleted: cursorPosition = 0
    }

    ChromeToolButton {
        anchors.right: parent.right
        anchors.rightMargin: 10
        y: row.supported ? 7 : 8
        width: 78
        height: 28
        enabled: row.supported
        raised: true
        text: qsTr("Browse…")
        onClicked: row.browseRequested()
    }

    Text {
        visible: row.supported
        x: 238
        y: 40
        text: row.gamePath === ""
            ? qsTr("Not configured")
            : row.pathValid
                ? qsTr("Executable detected")
                : qsTr("%1 not found").arg(row.expectedExecutable)
        color: row.gamePath !== "" && !row.pathValid
            ? Theme.Theme.error
            : Theme.Theme.textFaint
        font.family: Theme.Theme.monoFont
        font.pixelSize: 9
    }
}
