import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

/*
 * Every button in the chrome.
 *
 * Three shapes, chosen with a flag rather than by overriding `background` at the
 * call site: a ghost (transparent until hovered), a raised chrome button, and
 * the green primary. Overriding the background outside this file is what killed
 * the hover and pressed states before — the variants exist so nobody has to.
 */
ToolButton {
    id: control

    property bool bordered: true
    // The green action. Dark ink, brighter on hover, pressed a shade down.
    property bool primary: false
    // A destructive action uses the error colour as its fill while preserving
    // the same bevel, hover, pressed, and disabled behavior as other variants.
    property bool destructive: false
    // Sits on raised chrome with a bevel instead of being transparent at rest.
    property bool raised: false
    // A close mark tucked into a panel header takes no fill: a slab behind a tiny
    // glyph reads as a smudge, so the mark brightens on its own instead.
    property bool hoverFill: true
    // A selected segment sits in a sunken well instead of lighting up.
    property bool sunken: false
    // Set iconKind to draw a stroked glyph instead of rendering text. The UI
    // font has no arrow or view-mode glyphs, so those must always be drawn.
    property string iconKind: ""

    readonly property bool framed: bordered || raised || primary || destructive

    readonly property color fillBase: destructive
        ? Theme.Theme.error
        : primary
            ? Theme.Theme.selection
            : raised ? Theme.Theme.chromeRaised : "transparent"
    readonly property color fillHover: destructive
        ? Qt.lighter(Theme.Theme.error, 1.11)
        : primary
            ? Qt.lighter(Theme.Theme.selection, 1.11)
            : Theme.Theme.hoverChrome
    readonly property color fillDown: destructive
        ? Qt.darker(Theme.Theme.error, 1.12)
        : primary
            ? Qt.darker(Theme.Theme.selection, 1.12)
            : Theme.Theme.borderSoft

    property color foreground: !enabled
        ? Theme.Theme.textFaint
        : primary || destructive
            ? Theme.Theme.selectionText
            : down
                ? Theme.Theme.textRow
                : hovered ? Theme.Theme.text : Theme.Theme.textDim

    implicitWidth: 32
    implicitHeight: 28
    padding: 0
    hoverEnabled: true

    background: Rectangle {
        color: !control.enabled
            ? (control.framed ? Theme.Theme.chromeRaised : "transparent")
            : control.sunken
                ? Theme.Theme.insetBg
                : !control.hoverFill
                    ? control.fillBase
                    : control.down
                        ? control.fillDown
                        : control.hovered
                            ? control.fillHover
                            : control.fillBase
        border.width: control.framed ? 1 : 0
        border.color: Theme.Theme.borderHard

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.margins: 1
            height: 1
            visible: control.framed && !control.sunken && !control.down
            color: (control.primary || control.destructive) && control.enabled
                ? Qt.rgba(1, 1, 1, 0.4)
                : Theme.Theme.bevel
        }
    }

    contentItem: Item {
        ChromeIcon {
            anchors.centerIn: parent
            visible: control.iconKind !== ""
            kind: control.iconKind
            stroke: control.foreground
        }
        Text {
            anchors.fill: parent
            visible: control.iconKind === ""
            text: control.text
            color: control.foreground
            font.family: Theme.Theme.uiFont
            font.pixelSize: Theme.Theme.fontSize
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
        }
    }
}
