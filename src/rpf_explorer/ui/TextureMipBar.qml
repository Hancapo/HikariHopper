import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: mipBar

    required property var bridge

    implicitHeight: Theme.Theme.textureMipBarHeight
    color: Theme.Theme.chromeBg

    Rectangle {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
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

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 12
        anchors.rightMargin: 12
        spacing: 10

        Text {
            Layout.preferredWidth: 72
            text: qsTr("MIP %1 / %2")
                .arg(mipBar.bridge.mipLevel)
                .arg(Math.max(0, mipBar.bridge.mipCount - 1))
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
        }

        Slider {
            id: mipSlider
            readonly property int thumbWidth: 10

            Layout.fillWidth: true
            Layout.preferredHeight: 24
            from: 0
            to: Math.max(0, mipBar.bridge.mipCount - 1)
            stepSize: 1
            snapMode: Slider.SnapAlways
            enabled: mipBar.bridge.mipCount > 1
            activeFocusOnTab: true
            Accessible.name: qsTr("Mip level")
            onMoved: mipBar.bridge.setMipLevel(Math.round(value))

            Binding {
                target: mipSlider
                property: "value"
                value: mipBar.bridge.mipLevel
                when: !mipSlider.pressed
            }

            background: Item {
                x: mipSlider.leftPadding
                y: mipSlider.topPadding
                    + Math.round((mipSlider.availableHeight - height) / 2)
                width: mipSlider.availableWidth
                height: 20
                implicitWidth: 240
                implicitHeight: 20

                Rectangle {
                    x: mipSlider.thumbWidth / 2
                    anchors.verticalCenter: parent.verticalCenter
                    width: Math.max(0, parent.width - mipSlider.thumbWidth)
                    height: 3
                    color: Theme.Theme.insetBg
                    border.width: 1
                    border.color: Theme.Theme.borderHard
                }

                Rectangle {
                    x: mipSlider.thumbWidth / 2
                    anchors.verticalCenter: parent.verticalCenter
                    width: Math.round(
                        Math.max(0, parent.width - mipSlider.thumbWidth)
                        * mipSlider.visualPosition
                    )
                    height: 1
                    color: Theme.Theme.textDim
                }

                Repeater {
                    model: mipBar.bridge.mipCount
                    delegate: Rectangle {
                        required property int index
                        x: Math.round(
                            mipSlider.thumbWidth / 2
                            + (mipBar.bridge.mipCount > 1
                                ? index * (parent.width - mipSlider.thumbWidth)
                                    / (mipBar.bridge.mipCount - 1)
                                : 0)
                        )
                        anchors.verticalCenter: parent.verticalCenter
                        width: 1
                        height: 7
                        color: Theme.Theme.guide
                    }
                }
            }

            handle: Rectangle {
                x: Math.round(
                    mipSlider.leftPadding
                    + mipSlider.visualPosition * (mipSlider.availableWidth - width)
                )
                y: mipSlider.topPadding
                    + Math.round((mipSlider.availableHeight - height) / 2)
                width: mipSlider.thumbWidth
                height: 16
                color: mipSlider.pressed
                    ? Theme.Theme.textRow
                    : Theme.Theme.chromeRaised
                border.width: 1
                border.color: mipSlider.activeFocus
                    ? Theme.Theme.textDim
                    : Theme.Theme.border
            }
        }

        Text {
            Layout.preferredWidth: 92
            text: qsTr("%1 × %2")
                .arg(mipBar.bridge.previewWidth)
                .arg(mipBar.bridge.previewHeight)
            color: Theme.Theme.textRow
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            horizontalAlignment: Text.AlignRight
        }
    }
}
