import QtQuick
import "entry_text.js" as EntryText
import "theme" as Theme

Item {
    id: cell

    required property var bridge
    required property string name
    required property string kind
    required property string sizeLabel
    required property int childCount
    required property bool isDirectory
    required property bool selected
    required property int index

    width: Theme.Theme.gridCellWidth
    height: Theme.Theme.gridCellHeight

    Rectangle {
        anchors.fill: parent
        anchors.margins: Theme.Theme.gridCellInset
        color: cell.selected
            ? Theme.Theme.selection
            : pointer.containsMouse ? Theme.Theme.hoverBg : "transparent"
    }

    FileGlyph {
        anchors.horizontalCenter: parent.horizontalCenter
        y: 16
        width: Theme.Theme.gridGlyphSize
        height: Theme.Theme.gridGlyphSize
        kind: cell.isDirectory
            ? "folder"
            : (cell.kind.indexOf("Package") >= 0 ? "archive" : "file")
        fileKind: cell.kind
        selected: cell.selected
    }

    Text {
        x: 9
        y: 57
        width: parent.width - 18
        height: 32
        text: EntryText.markMatch(
            cell.name,
            cell.bridge.searchQuery,
            cell.selected ? Theme.Theme.selectionText : Theme.Theme.brass
        )
        textFormat: Text.StyledText
        color: cell.selected ? Theme.Theme.selectionText : Theme.Theme.textRow
        font.family: Theme.Theme.monoFont
        font.pixelSize: Theme.Theme.smallFontSize
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignTop
        wrapMode: Text.WrapAnywhere
        maximumLineCount: 2
        elide: Text.ElideRight
    }

    Text {
        x: 8
        y: 92
        width: parent.width - 16
        text: cell.childCount > 0 ? qsTr("%1 items").arg(cell.childCount) : cell.sizeLabel
        color: cell.selected ? Theme.Theme.selectionInk : Theme.Theme.textFaint
        font.family: Theme.Theme.monoFont
        font.pixelSize: 9
        horizontalAlignment: Text.AlignHCenter
        elide: Text.ElideRight
    }

    EntryPointerArea {
        id: pointer
        bridge: cell.bridge
        entryIndex: cell.index
        entrySelected: cell.selected
        focusTarget: cell.GridView.view
        accessibleName: cell.name
    }
}
