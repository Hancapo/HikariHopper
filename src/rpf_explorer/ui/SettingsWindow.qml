import QtQuick
import QtQuick.Layouts
import "theme" as Theme

/*
 * Settings — a placeholder.
 *
 * The frame is the real design language, so whatever lands here later drops
 * straight in. What it deliberately does NOT have is a set of pretend toggles:
 * there is nothing to configure yet, and a window full of controls that change
 * nothing would be exactly the kind of interface that lies (DESIGN.md 11, 14).
 */
ToolWindow {
    id: window

    width: 520
    height: 320
    title: qsTr("Settings")
    heading: qsTr("SETTINGS")

    ColumnLayout {
        anchors.centerIn: parent
        width: Math.min(360, parent.width)
        spacing: 10

        Text {
            Layout.alignment: Qt.AlignHCenter
            text: qsTr("Nothing to configure yet")
            color: Theme.Theme.textDim
            font.family: Theme.Theme.monoFont
            font.pixelSize: Theme.Theme.fontSize
        }
    }
}
