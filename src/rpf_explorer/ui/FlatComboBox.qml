import QtQuick
import QtQuick.Controls.Basic
import "theme" as Theme

pragma ComponentBehavior: Bound

/*
 * A square, sunken combo box in the house style: an inset well, a drawn caret,
 * and a popup built like RetroMenu. Entries may be disabled — the model can set
 * `supported: false` and give a `note` explaining why, which is how the app
 * lists titles it cannot open without pretending it can.
 */
ComboBox {
    id: control

    textRole: "label"
    implicitHeight: 28
    padding: 0
    hoverEnabled: true
    font.family: Theme.Theme.monoFont
    font.pixelSize: Theme.Theme.fontSize

    // Whether the entry the user is currently sitting on can actually be opened.
    readonly property bool currentSupported:
        control.currentIndex < 0 || control.model[control.currentIndex].supported !== false
    readonly property string currentNote:
        control.currentIndex < 0 ? "" : (control.model[control.currentIndex].note || "")

    background: Rectangle {
        color: control.hovered && !control.popup.visible
            ? Qt.lighter(Theme.Theme.insetBg, 1.35)
            : Theme.Theme.insetBg
        border.width: 1
        border.color: control.activeFocus || control.popup.visible
            ? Theme.Theme.accent
            : control.hovered ? Theme.Theme.border : Theme.Theme.borderHard
    }

    contentItem: Text {
        leftPadding: 10
        rightPadding: 8
        text: control.displayText
        color: control.currentSupported ? Theme.Theme.text : Theme.Theme.textFaint
        font: control.font
        verticalAlignment: Text.AlignVCenter
        elide: Text.ElideRight
    }

    indicator: Item {
        x: control.width - width - 8
        y: (control.height - height) / 2
        implicitWidth: 14
        implicitHeight: 14
        ChromeIcon {
            anchors.fill: parent
            kind: "caretDown"
            stroke: control.hovered || control.popup.visible
                ? Theme.Theme.text
                : Theme.Theme.textDim
            thickness: 1.6
        }
    }

    popup: Popup {
        y: control.height
        width: control.width
        implicitHeight: Math.min(contentItem.implicitHeight + 8, 280)
        padding: 4

        background: Rectangle {
            color: Theme.Theme.chromeRaised
            border.width: 1
            border.color: Theme.Theme.borderHard
            Rectangle {
                anchors.fill: parent
                anchors.margins: 1
                color: "transparent"
                border.width: 1
                border.color: Theme.Theme.bevel
            }
        }

        contentItem: ListView {
            clip: true
            implicitHeight: contentHeight
            model: control.delegateModel
            currentIndex: control.highlightedIndex
            boundsBehavior: Flickable.StopAtBounds
        }
    }

    delegate: ItemDelegate {
        id: option
        required property var modelData
        required property int index

        readonly property bool supported: option.modelData.supported !== false

        width: ListView.view ? ListView.view.width : control.width
        height: 24
        padding: 0
        enabled: option.supported
        highlighted: control.highlightedIndex === option.index

        background: Rectangle {
            color: option.highlighted && option.supported ? Theme.Theme.accent : "transparent"
        }

        contentItem: Row {
            leftPadding: 10
            rightPadding: 10
            spacing: 8
            Text {
                width: option.width - 20 - (noteLabel.visible ? noteLabel.width + 8 : 0)
                height: option.height
                text: option.modelData.label
                color: !option.supported
                    ? Theme.Theme.textFaint
                    : option.highlighted ? Theme.Theme.text : Theme.Theme.textRow
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.fontSize
                verticalAlignment: Text.AlignVCenter
                elide: Text.ElideRight
            }
            Text {
                id: noteLabel
                height: option.height
                visible: !option.supported && option.modelData.note !== undefined
                text: option.modelData.note || ""
                color: Theme.Theme.textFaint
                font.family: Theme.Theme.monoFont
                font.pixelSize: Theme.Theme.smallFontSize
                verticalAlignment: Text.AlignVCenter
            }
        }
    }
}
