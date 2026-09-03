import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: toolbar

    required property var bridge
    required property bool fitMode
    required property bool checkerboardVisible
    required property bool filteredPreview
    required property string zoomPercent

    signal fitRequested()
    signal actualSizeRequested()
    signal zoomInRequested()
    signal zoomOutRequested()
    signal checkerboardToggled()
    signal filteredPreviewToggled()

    readonly property var channels: [
        { value: "rgba", label: "RGBA", width: 46 },
        { value: "r", label: "R", width: 27 },
        { value: "g", label: "G", width: 27 },
        { value: "b", label: "B", width: 27 },
        { value: "a", label: "A", width: 27 }
    ]

    implicitHeight: Theme.Theme.textureToolbarHeight
    color: Theme.Theme.chromeBg

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
        anchors.rightMargin: 6
        spacing: 3

        Text {
            Layout.fillWidth: true
            text: toolbar.bridge.selectedName
            color: Theme.Theme.text
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.fontSize
            font.bold: true
            elide: Text.ElideRight
        }

        Text {
            Layout.preferredWidth: 58
            Layout.fillHeight: true
            text: qsTr("CHANNEL")
            color: Theme.Theme.textFaint
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
        }

        Repeater {
            model: toolbar.channels
            delegate: ChromeToolButton {
                id: channelButton
                required property var modelData
                Layout.preferredWidth: channelButton.modelData.width
                Layout.preferredHeight: 26
                text: channelButton.modelData.label
                sunken: toolbar.bridge.channel === channelButton.modelData.value
                raised: !sunken
                Accessible.name: qsTr("Show %1 channel").arg(channelButton.modelData.label)
                onClicked: toolbar.bridge.setChannel(channelButton.modelData.value)
            }
        }

        Rectangle {
            Layout.preferredWidth: 1
            Layout.preferredHeight: 20
            color: Theme.Theme.border
        }

        ChromeToolButton {
            Layout.preferredWidth: 30
            Layout.preferredHeight: 26
            iconKind: "checkerboard"
            sunken: toolbar.checkerboardVisible
            raised: !sunken
            Accessible.name: qsTr("Toggle transparency grid")
            onClicked: toolbar.checkerboardToggled()
        }

        ChromeToolButton {
            Layout.preferredWidth: 58
            Layout.preferredHeight: 26
            text: qsTr("Filtered")
            sunken: toolbar.filteredPreview
            raised: !sunken
            Accessible.name: qsTr("Toggle filtered texture preview")
            onClicked: toolbar.filteredPreviewToggled()
        }

        Rectangle {
            Layout.preferredWidth: 1
            Layout.preferredHeight: 20
            color: Theme.Theme.border
        }

        ChromeToolButton {
            Layout.preferredWidth: 44
            Layout.preferredHeight: 26
            text: qsTr("Fit")
            sunken: toolbar.fitMode
            raised: !sunken
            onClicked: toolbar.fitRequested()
        }

        ChromeToolButton {
            Layout.preferredWidth: 36
            Layout.preferredHeight: 26
            text: "1:1"
            raised: true
            Accessible.name: qsTr("Actual size")
            onClicked: toolbar.actualSizeRequested()
        }

        ChromeToolButton {
            Layout.preferredWidth: 27
            Layout.preferredHeight: 26
            iconKind: "minus"
            raised: true
            Accessible.name: qsTr("Zoom out")
            onClicked: toolbar.zoomOutRequested()
        }

        Rectangle {
            Layout.preferredWidth: 54
            Layout.preferredHeight: 26
            color: Theme.Theme.insetBg
            border.width: 1
            border.color: Theme.Theme.borderHard
            Text {
                anchors.fill: parent
                text: toolbar.zoomPercent
                color: Theme.Theme.textDim
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                horizontalAlignment: Text.AlignHCenter
                verticalAlignment: Text.AlignVCenter
            }
        }

        ChromeToolButton {
            Layout.preferredWidth: 27
            Layout.preferredHeight: 26
            iconKind: "plus"
            raised: true
            Accessible.name: qsTr("Zoom in")
            onClicked: toolbar.zoomInRequested()
        }
    }
}
