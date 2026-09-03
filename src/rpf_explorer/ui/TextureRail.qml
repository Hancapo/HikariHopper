import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

pragma ComponentBehavior: Bound

Rectangle {
    id: rail

    required property var bridge

    signal resizeRequested()
    signal mipmapsRequested()
    signal formatRequested()
    signal alphaRepairRequested()
    signal renameRequested()
    signal removeRequested()

    color: Theme.Theme.panelBg

    function openContextMenu(item, localX, localY) {
        const point = item.mapToItem(rail, localX, localY)
        textureContextMenu.x = point.x
        textureContextMenu.y = point.y
        textureContextMenu.open()
    }

    Rectangle {
        id: header
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
        height: Theme.Theme.headerHeight
        color: Theme.Theme.chromeRaised

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
        Text {
            x: 12
            anchors.verticalCenter: parent.verticalCenter
            text: qsTr("TEXTURES  ·  %1").arg(rail.bridge.textureCount)
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
        }
    }

    ListView {
        id: textureList
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: header.bottom
        anchors.bottom: parent.bottom
        model: rail.bridge.texturesModel
        currentIndex: rail.bridge.selectedIndex
        reuseItems: true
        // The delegates genuinely scroll beyond this viewport; without a clip
        // they paint through the fixed header and the source bar above it.
        clip: true
        activeFocusOnTab: true
        boundsBehavior: Flickable.StopAtBounds
        keyNavigationEnabled: false
        Accessible.name: qsTr("Textures")
        onCurrentIndexChanged: {
            if (currentIndex >= 0)
                positionViewAtIndex(currentIndex, ListView.Contain)
        }

        delegate: Rectangle {
            id: textureDelegate

            required property int index
            required property string name
            required property string dimensions
            required property string formatName
            required property string thumbnailUrl
            required property int mipCount

            readonly property bool selected: textureDelegate.index === rail.bridge.selectedIndex

            width: ListView.view.width
            height: Theme.Theme.textureRowHeight
            color: selected
                ? Theme.Theme.selection
                : hover.hovered ? Theme.Theme.hoverBg : "transparent"

            HoverHandler { id: hover }

            Rectangle {
                x: 7
                y: 6
                width: Theme.Theme.textureThumbnailSize
                height: Theme.Theme.textureThumbnailSize
                color: Theme.Theme.insetBg
                border.width: 1
                border.color: textureDelegate.selected
                    ? Theme.Theme.selectionInk
                    : Theme.Theme.border

                TextureCheckerboard {
                    anchors.fill: parent
                    anchors.margins: 1
                    cellSize: 8
                }

                Image {
                    anchors.fill: parent
                    anchors.margins: 1
                    source: textureDelegate.thumbnailUrl
                    sourceSize.width: Theme.Theme.textureThumbnailSize * 2
                    sourceSize.height: Theme.Theme.textureThumbnailSize * 2
                    fillMode: Image.PreserveAspectFit
                    asynchronous: true
                    cache: false
                    visible: textureDelegate.thumbnailUrl !== ""
                }

                LucideIcon {
                    anchors.centerIn: parent
                    width: 18
                    height: 18
                    visible: textureDelegate.thumbnailUrl === ""
                    name: "image"
                    stroke: textureDelegate.selected
                        ? Theme.Theme.selectionInk
                        : Theme.Theme.inkAsset
                    Accessible.ignored: true
                }
            }

            Text {
                x: 68
                y: 10
                width: parent.width - 80
                text: textureDelegate.name
                color: textureDelegate.selected
                    ? Theme.Theme.selectionText
                    : Theme.Theme.textRow
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.fontSize
                font.bold: textureDelegate.selected
                elide: Text.ElideRight
            }

            Text {
                x: 68
                y: 33
                text: textureDelegate.dimensions
                color: textureDelegate.selected
                    ? Theme.Theme.selectionInk
                    : Theme.Theme.textDim
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
            }

            Row {
                anchors.right: parent.right
                anchors.rightMargin: textureScrollBar.enabled
                    ? Theme.Theme.scrollbarWidth + 6
                    : 12
                y: 33
                spacing: 12

                Text {
                    text: textureDelegate.formatName
                    color: textureDelegate.selected
                        ? Theme.Theme.selectionInk
                        : Theme.Theme.textFaint
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: Theme.Theme.smallFontSize
                }

                Text {
                    text: qsTr("%1 MIPS").arg(textureDelegate.mipCount)
                    color: textureDelegate.selected
                        ? Theme.Theme.selectionInk
                        : Theme.Theme.textFaint
                    font.family: Theme.Theme.monoFont
                    font.pixelSize: Theme.Theme.smallFontSize
                }
            }

            MouseArea {
                anchors.fill: parent
                cursorShape: Qt.PointingHandCursor
                acceptedButtons: Qt.LeftButton | Qt.RightButton
                onPressed: mouse => {
                    if (mouse.button === Qt.RightButton)
                        rail.bridge.selectTexture(textureDelegate.index)
                }
                onClicked: mouse => {
                    if (mouse.button === Qt.LeftButton) {
                        rail.bridge.selectTexture(textureDelegate.index)
                        textureList.forceActiveFocus()
                    } else if (mouse.button === Qt.RightButton) {
                        textureList.forceActiveFocus()
                        rail.openContextMenu(textureDelegate, mouse.x, mouse.y)
                    }
                }
            }
        }

        ScrollBar.vertical: QuietScrollBar {
            id: textureScrollBar
            accessibleName: qsTr("Texture list scroll bar")
        }

        Keys.onUpPressed: {
            if (rail.bridge.selectedIndex > 0)
                rail.bridge.selectTexture(rail.bridge.selectedIndex - 1)
        }
        Keys.onDownPressed: {
            if (rail.bridge.selectedIndex + 1 < rail.bridge.textureCount)
                rail.bridge.selectTexture(rail.bridge.selectedIndex + 1)
        }
        Keys.onPressed: event => {
            if (event.key === Qt.Key_Home && rail.bridge.textureCount > 0) {
                rail.bridge.selectTexture(0)
                event.accepted = true
            } else if (event.key === Qt.Key_End && rail.bridge.textureCount > 0) {
                rail.bridge.selectTexture(rail.bridge.textureCount - 1)
                event.accepted = true
            } else if (
                event.key === Qt.Key_Menu
                || (event.key === Qt.Key_F10 && (event.modifiers & Qt.ShiftModifier))
            ) {
                const item = itemAtIndex(rail.bridge.selectedIndex)
                if (item) {
                    rail.openContextMenu(item, 76, item.height / 2)
                    event.accepted = true
                }
            }
        }
    }

    TextureContextMenu {
        id: textureContextMenu
        parent: rail
        bridge: rail.bridge
        onResizeRequested: rail.resizeRequested()
        onMipmapsRequested: rail.mipmapsRequested()
        onFormatRequested: rail.formatRequested()
        onAlphaRepairRequested: rail.alphaRepairRequested()
        onRenameRequested: rail.renameRequested()
        onRemoveRequested: rail.removeRequested()
    }
}
