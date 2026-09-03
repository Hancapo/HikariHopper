import QtQuick
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: strip
    required property var tabs
    color: Theme.Theme.chromeBg
    Layout.fillWidth: true
    Layout.preferredHeight: Theme.Theme.tabHeight

    RowLayout {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        anchors.leftMargin: 7
        height: parent.height - 3
        spacing: 2

        ListView {
            id: tabList
            Layout.preferredWidth: Math.min(contentWidth, strip.width - 45)
            Layout.maximumWidth: strip.width - 45
            Layout.fillHeight: true
            orientation: ListView.Horizontal
            model: strip.tabs
            spacing: 2
            clip: true
            currentIndex: strip.tabs.activeIndex
            onCurrentIndexChanged: positionViewAtIndex(currentIndex, ListView.Contain)

            delegate: Rectangle {
                id: tabDelegate
                required property int index
                required property string tabTitle
                required property bool tabActive

                width: Math.max(150, Math.min(220, titleLabel.implicitWidth + 66))
                height: tabList.height
                color: tabDelegate.tabActive
                    ? Theme.Theme.tabActive
                    : tabHover.hovered ? Theme.Theme.hoverChrome : Theme.Theme.tabBg
                border.width: 1
                border.color: tabDelegate.tabActive
                    ? Theme.Theme.borderAccent
                    : tabHover.hovered ? Theme.Theme.guide : Theme.Theme.border

                // A HoverHandler rather than a MouseArea: the close button sits on
                // top of this delegate and would win the hover grab from one.
                HoverHandler { id: tabHover }

                RowLayout {
                    anchors.fill: parent
                    anchors.leftMargin: 11
                    anchors.rightMargin: 4
                    spacing: 7

                    FileGlyph {
                        kind: tabDelegate.tabTitle.endsWith(".rpf") ? "archive" : "folder"
                    }

                    Text {
                        id: titleLabel
                        text: tabDelegate.tabTitle
                        color: tabDelegate.tabActive || tabHover.hovered
                            ? Theme.Theme.text
                            : Theme.Theme.textDim
                        font.family: Theme.Theme.monoFont
                        font.pixelSize: Theme.Theme.fontSize
                        font.bold: tabDelegate.tabActive
                        elide: Text.ElideRight
                        Layout.fillWidth: true
                    }

                    ChromeToolButton {
                        Layout.preferredWidth: 20
                        Layout.fillHeight: true
                        bordered: false
                        hoverFill: false
                        iconKind: "close"
                        Accessible.name: qsTr("Close %1").arg(tabDelegate.tabTitle)
                        onClicked: strip.tabs.closeTab(tabDelegate.index)
                    }
                }

                MouseArea {
                    anchors.left: parent.left
                    anchors.top: parent.top
                    anchors.bottom: parent.bottom
                    anchors.right: parent.right
                    anchors.rightMargin: 24
                    onClicked: strip.tabs.activateTab(tabDelegate.index)
                }
            }
        }

        ChromeToolButton {
            Layout.preferredWidth: 29
            Layout.fillHeight: true
            iconKind: "plus"
            Accessible.name: qsTr("New explorer tab")
            onClicked: strip.tabs.newTab()
        }

        Item { Layout.fillWidth: true }
        Item { Layout.preferredWidth: 7 }
    }

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        height: 1
        color: Theme.Theme.borderHard
    }
}
