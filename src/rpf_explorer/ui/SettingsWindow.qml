import QtQuick
import "theme" as Theme

ToolWindow {
    id: window

    required property var gamePaths

    width: 1080
    height: 688
    minimumWidth: 820
    minimumHeight: 620
    title: qsTr("Settings")
    heading: qsTr("SETTINGS")
    bodyMargin: 0
    footerText: qsTr("Changes are saved immediately")

    readonly property var groups: [
        { label: qsTr("Game paths"), note: qsTr("9 titles"), available: true },
        { label: qsTr("Explorer"), note: qsTr("Later"), available: false },
        { label: qsTr("Appearance"), note: qsTr("Later"), available: false }
    ]

    readonly property var futureGames: [
        qsTr("Grand Theft Auto III"),
        qsTr("Grand Theft Auto: Vice City"),
        qsTr("Grand Theft Auto: San Andreas"),
        qsTr("Bully: Scholarship Edition"),
        qsTr("Grand Theft Auto IV"),
        qsTr("Red Dead Redemption 2"),
        qsTr("Red Dead Redemption")
    ]

    Rectangle {
        id: groupRail

        anchors.left: parent.left
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        width: 196
        color: Theme.Theme.sidebarBg

        SettingsPanelHeader {
            id: groupsHeader

            width: parent.width
            label: qsTr("GROUPS")
        }

        Column {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: groupsHeader.bottom

            Repeater {
                model: window.groups

                delegate: SettingsGroupRow {
                    required property var modelData
                    required property int index

                    width: parent.width
                    label: modelData.label
                    note: modelData.note
                    available: modelData.available
                    selected: index === 0
                }
            }
        }

        Text {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.leftMargin: 12
            anchors.rightMargin: 12
            anchors.bottom: parent.bottom
            anchors.bottomMargin: 14
            text: qsTr("Additional groups will appear later.")
            color: Theme.Theme.textFaint
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.smallFontSize
            wrapMode: Text.WordWrap
        }
    }

    Rectangle {
        anchors.left: groupRail.right
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        width: 1
        color: Theme.Theme.borderHard
    }

    Item {
        id: optionsPanel

        anchors.left: groupRail.right
        anchors.leftMargin: 1
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.bottom: parent.bottom

        SettingsPanelHeader {
            width: parent.width
            label: qsTr("OPTIONS  /  GAME PATHS")
        }

        Text {
            x: 16
            y: 38
            text: qsTr("SUPPORTED")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
        }

        Column {
            x: 12
            y: 58
            width: parent.width - 24

            GamePathRow {
                width: parent.width
                gameName: qsTr("Grand Theft Auto V Legacy")
                gamePath: window.gamePaths.legacyPath
                expectedExecutable: "GTA5.exe"
                supported: true
                pathValid: window.gamePaths.legacyPathValid
                onPathCommitted: path => window.gamePaths.setLegacyPath(path)
                onBrowseRequested: window.gamePaths.browseLegacyPath()
            }

            GamePathRow {
                width: parent.width
                gameName: qsTr("Grand Theft Auto V Enhanced")
                gamePath: window.gamePaths.enhancedPath
                expectedExecutable: "GTA5_Enhanced.exe"
                supported: true
                pathValid: window.gamePaths.enhancedPathValid
                onPathCommitted: path => window.gamePaths.setEnhancedPath(path)
                onBrowseRequested: window.gamePaths.browseEnhancedPath()
            }
        }

        Text {
            x: 16
            y: 194
            text: qsTr("FUTURE SUPPORT")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.smallFontSize
            font.bold: true
            font.letterSpacing: 1
        }

        Column {
            x: 12
            y: 214
            width: parent.width - 24

            Repeater {
                model: window.futureGames

                delegate: GamePathRow {
                    required property string modelData

                    width: parent.width
                    gameName: modelData
                }
            }
        }
    }
}
